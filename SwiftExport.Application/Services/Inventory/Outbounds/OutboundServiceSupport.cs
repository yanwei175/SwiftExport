using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Application.Commands;

namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>统一出库模块访问、错误映射和分页格式检查，不持有业务事务。</summary>
public sealed class OutboundServiceSupport(ServiceBaseContext context, ICurrentUser user, IPermissionEvaluationService permissions)
{
    private readonly ILogger<OutboundServiceSupport> _logger = context.LoggerFactory.CreateLogger<OutboundServiceSupport>();

    /// <summary>包装出库用例结果；命令管道已授权时可跳过重复的模块权限检查。</summary>
    public async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken token, bool checkPermission = true)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (checkPermission && await AuthorizeAsync<T>(token) is { } refusal) return refusal;
            return ServiceResult<T>.Ok(await action());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (BusinessCommandException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (OutboundDraftException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex)
        {
            _logger.LogWarning(ex, "出库版本冲突，事务已回滚。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.VersionConflict, "出库单或明细已变化，请刷新后重试。", "ExpectedVersion");
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            _logger.LogError(ex, "出库数据库结构尚未对齐。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "出库数据库结构与当前实体不一致，请先完成数据库结构核对；本次操作未完成。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "出库操作结果未能确认。");
            return ServiceResult<T>.Fail(OutboundErrorCodes.OutcomeUnknown, "出库操作结果未能确认；写操作请使用原 CommandId 重试，不要重复创建单据。");
        }
    }

    /// <summary>在请求验证及命令回执读取之前核对身份与库存模块权限。</summary>
    public async Task<ServiceResult<T>?> AuthorizeAsync<T>(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        if (!await permissions.HasPermissionAsync(id, PermissionKeys.ModuleInventoryAccess, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号无库存模块访问权限。");
        return null;
    }

    /// <summary>限制出库列表和选品查询的分页上界。</summary>
    public static void ValidatePage(PagedRequestBase? request)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            throw new OutboundDraftException("分页参数无效，页大小为 1–200，关键词最多 200 字。");
    }
}

/// <summary>可安全显示给用户的出库业务拒绝，携带稳定错误代码和可选字段路径。</summary>
public sealed class OutboundDraftException(string message, string code = ServiceErrorCodes.BusinessBlocked, string? field = null) : Exception(message)
{
    /// <summary>客户端识别的错误代码。</summary>
    public string Code { get; } = code;
    /// <summary>发生验证错误的请求字段。</summary>
    public string? Field { get; } = field;
}

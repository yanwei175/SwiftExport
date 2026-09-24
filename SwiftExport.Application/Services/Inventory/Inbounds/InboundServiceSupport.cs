using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Application.Services.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>集中入库用例共用的模块访问检查、错误映射及分页上界。</summary>
public sealed class InboundServiceSupport(ICurrentUser user, IUserPermissionService permissions,
    ILogger<InboundServiceSupport> logger)
{
    /// <summary>统一读取与写入访问权限及错误包装；数据库技术信息仅写服务端日志。</summary>
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
        catch (DraftException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex)
        {
            logger.LogWarning(ex, "入库草稿版本冲突，事务已回滚。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.VersionConflict, "入库单或明细已变化，请刷新后重试。", "ExpectedVersion");
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            logger.LogError(ex, "入库数据库结构尚未对齐。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "入库数据库结构与当前实体不一致，请先完成数据库结构核对；本次操作未完成。");
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            logger.LogWarning(ex, "存在来源或反向关系未核实的正式入库事实。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "存在未完成来源或反向完整性核对的历史入库，无法可靠计算净收货量，请先核实历史事实。");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "入库操作结果未能确认。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, "入库操作结果未能确认；写操作请使用原 CommandId 重试，不要重复创建单据。");
        }
    }

    /// <summary>在验证和回执读取之前核对身份与库存模块权限。</summary>
    public async Task<ServiceResult<T>?> AuthorizeAsync<T>(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        if (!await permissions.HasPermissionAsync(id, PermissionKeys.ModuleInventoryAccess, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号无库存模块访问权限。");
        return null;
    }

    /// <summary>验证分页上界，避免无界查询和偏移量溢出。</summary>
    public static void ValidatePage(PagedRequestBase? request)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            throw new DraftException("分页参数无效，页大小为 1–200，关键词最多 200 字。");
    }

}

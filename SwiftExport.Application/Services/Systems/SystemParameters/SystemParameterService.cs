using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Systems.SystemParameters;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.SystemParameters;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Systems.SystemParameters;

namespace SwiftExport.Application.Services.Systems.SystemParameters;

/// <summary>维护轻量全局参数，定义保存在代码中，数据库只保存值及并发版本。</summary>
public sealed class SystemParameterService(ISystemParameterRepository repository, ICurrentUser currentUser,
    IPermissionEvaluationService permissions, ServiceBaseContext context) : ISystemParameterService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<SystemParameterService>();

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(false, cancellationToken);
        if (access is not null) return ServiceResult<IReadOnlyList<SystemParameterDto>>.Fail(access.Value.Code, access.Value.Message);
        var parameter = await repository.FindAsync(SystemParameterKeys.ProductImagesBaseUrl, cancellationToken);
        return ServiceResult<IReadOnlyList<SystemParameterDto>>.Ok([ToDto(parameter)]);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, UpdateSystemParameterRequest request, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(true, cancellationToken);
        if (access is not null) return ServiceResult<SystemParameterDto>.Fail(access.Value.Code, access.Value.Message);
        if (key != SystemParameterKeys.ProductImagesBaseUrl || request is null || request.ExpectedVersion < 0)
            return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Validation, "参数键或预期版本无效。");
        if (!ProductImageAddress.TryNormalizeBaseUrl(request.Value, out var baseUri))
            return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Validation,
                "请输入不含凭据、查询、片段或 Thumbnails 目录的 HTTP/HTTPS 原图目录地址（最多 2048 字符）。", "Value");

        var session = context.Session;
        if (session.Transaction is not null) return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Unexpected, "当前请求已有未完成事务。");
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            var parameter = new SystemParameter { ParameterKey = key, ParameterValue = baseUri.AbsoluteUri };
            var saved = request.ExpectedVersion == 0
                ? await repository.InsertAsync(parameter, cancellationToken)
                : await repository.UpdateAsync(parameter, request.ExpectedVersion, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!saved)
            {
                Rollback(transaction);
                return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.VersionConflict, "参数已由其他人修改，请重新读取后再保存。");
            }
            transaction.Commit();
            return ServiceResult<SystemParameterDto>.Ok(ToDto(parameter));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        { Rollback(transaction); throw new OperationCanceledException(cancellationToken); }
        catch (Exception exception)
        {
            Rollback(transaction);
            _logger.LogError(exception, "系统参数保存失败，Key={Key}", key);
            return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Unexpected, "参数保存结果无法确认，请刷新核对后再操作。");
        }
        finally
        {
            session.Transaction = null;
            try { transaction.Dispose(); }
            catch (Exception exception) { _logger.LogError(exception, "系统参数事务释放失败"); }
        }
    }

    /// <summary>读取实时活动账号投影，拒绝已停用账号及非管理员写入。</summary>
    private async Task<(string Code, string Message)?> AuthorizeAsync(bool write, CancellationToken cancellationToken)
    {
        var uid = currentUser.UserId ?? Guid.Empty;
        var projection = currentUser.IsAuthenticated && uid != Guid.Empty
            ? await permissions.GetProjectionAsync(uid, cancellationToken) : null;
        if (projection is null) return (ServiceErrorCodes.Unauthenticated, "登录状态无效或账号已停用。");
        if (write && !projection.IsAdmin) return (ServiceErrorCodes.Forbidden, "仅管理员可以维护系统参数。");
        return null;
    }

    /// <summary>从代码定义与当前数据库值构造公开参数，未配置时版本为 0。</summary>
    private static SystemParameterDto ToDto(SystemParameter? parameter) => new()
    {
        Key = SystemParameterKeys.ProductImagesBaseUrl, DisplayName = "产品图片基础地址",
        Description = "产品原图目录的公开 HTTP/HTTPS 地址；缩略图位于其 Thumbnails 子目录。修改此值不会搬迁文件或改写已有产品地址。",
        Value = parameter?.ParameterValue ?? string.Empty, Version = parameter?.Version ?? 0, IsConfigured = parameter is not null
    };

    /// <summary>尽力清理本用例事务，保留原始保存或取消失败。</summary>
    private void Rollback(IDbTransaction transaction)
    {
        try { transaction.Rollback(); }
        catch (Exception exception) { _logger.LogError(exception, "系统参数事务回滚失败"); }
    }
}

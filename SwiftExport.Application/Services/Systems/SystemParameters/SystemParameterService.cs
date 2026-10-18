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
using System.Data;
using SwiftExport.Application.Services.SalesOrders.Draft;
using System.Globalization;

namespace SwiftExport.Application.Services.Systems.SystemParameters;

/// <summary>维护轻量全局参数，定义保存在代码中，数据库只保存值及并发版本。</summary>
public sealed class SystemParameterService(ISystemParameterRepository repository, ICurrentUser currentUser,
    IPermissionEvaluationService permissions, ServiceBaseContext context) : ISystemParameterService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<SystemParameterService>();
    /// <summary>注册参数的说明及值格式；界面直接按读取结果逐行维护，不提供任意增删键。</summary>
    private sealed record ParameterDefinition(string Key, string DisplayName, string Description, bool AllowLocalPath, bool IsInteger = false);
    private static readonly ParameterDefinition[] Definitions =
    [
        new(SystemParameterKeys.ProductImagesBaseUrl, "产品图片重建基址",
            "重建图片引用使用的 HTTP/HTTPS 原图目录、Windows 本地绝对目录或 UNC 共享目录；修改此值不会改写已有产品图片引用。", true),
        new(SystemParameterKeys.ProductImagesUploadBaseUrl, "产品图片上传地址",
            "上传图片后公开访问的 HTTP/HTTPS 原图目录；缩略图位于其 Thumbnails 子目录。上传请求仍发往当前客户端配置的 API。", false),
        new(SystemParameterKeys.SalesDefaultDeliveryDays, "销售默认交期天数",
            "新建订单未指定交期时，按订单日期加此自然日天数；0 表示同日，已保存订单不随参数变化。", false, true)
    ];

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(false, cancellationToken);
        if (access is not null) return ServiceResult<IReadOnlyList<SystemParameterDto>>.Fail(access.Value.Code, access.Value.Message);
        var parameters = new Dictionary<string, SystemParameter?>();
        foreach (var definition in Definitions)
            parameters[definition.Key] = await repository.FindAsync(definition.Key, cancellationToken);
        // 升级后尚未单独配置上传地址时，展示原有网络基址作为待保存建议；不写库或沿用本地路径。
        var legacyBase = parameters[SystemParameterKeys.ProductImagesBaseUrl]?.ParameterValue;
        var fallback = ProductImageAddress.TryNormalizeBaseUrl(legacyBase!, out var legacyUri) ? legacyUri.AbsoluteUri : string.Empty;
        return ServiceResult<IReadOnlyList<SystemParameterDto>>.Ok(Definitions.Select(definition => ToDto(definition,
            parameters[definition.Key], definition.IsInteger ? SalesOrderDefaultDeliveryDatePolicy.InitialDays.ToString(CultureInfo.InvariantCulture)
                : definition.Key == SystemParameterKeys.ProductImagesUploadBaseUrl ? fallback : string.Empty)).ToArray());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, UpdateSystemParameterRequest request, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(true, cancellationToken);
        if (access is not null) return ServiceResult<SystemParameterDto>.Fail(access.Value.Code, access.Value.Message);
        var definition = Definitions.FirstOrDefault(item => item.Key == key);
        if (definition is null || request is null || request.ExpectedVersion < 0)
            return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Validation, "参数键或预期版本无效。");
        string normalized;
        var valid = definition.IsInteger ? TryNormalizeDays(request.Value, out normalized) : definition.AllowLocalPath
            ? ProductImageAddress.TryNormalizeBaseAddress(request.Value, out normalized)
            : TryNormalizePublicUrl(request.Value, out normalized);
        if (!valid)
            return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Validation,
                definition.IsInteger ? "销售默认交期天数必须为非负整数（自然日，0 表示同日）。" : definition.AllowLocalPath
                    ? "请输入 HTTP/HTTPS 原图目录或 Windows 本地、UNC 绝对目录，不允许相对路径（最多 2048 字符）。"
                    : "请输入不含凭据、查询、片段或 Thumbnails 目录的 HTTP/HTTPS 原图目录地址（最多 2048 字符）。", "Value");

        var session = context.Session;
        if (session.Transaction is not null) return ServiceResult<SystemParameterDto>.Fail(ServiceErrorCodes.Unexpected, "当前请求已有未完成事务。");
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            var parameter = new SystemParameter { ParameterKey = key, ParameterValue = normalized };
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
            return ServiceResult<SystemParameterDto>.Ok(ToDto(definition, parameter));
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
    private static SystemParameterDto ToDto(ParameterDefinition definition, SystemParameter? parameter, string fallback = "") => new()
    {
        Key = definition.Key,
        DisplayName = definition.DisplayName,
        Description = definition.Description,
        Value = parameter?.ParameterValue ?? fallback,
        Version = parameter?.Version ?? 0,
        IsConfigured = parameter is not null
    };

    /// <summary>上传地址始终规范为公开网络目录，不接受重建用的本地目录。</summary>
    private static bool TryNormalizePublicUrl(string value, out string normalized)
    {
        var valid = ProductImageAddress.TryNormalizeBaseUrl(value, out var baseUri);
        normalized = valid ? baseUri.AbsoluteUri : string.Empty;
        return valid;
    }

    /// <summary>规范整数参数的文本格式，保存为不带空白或前导零的自然日天数。</summary>
    private static bool TryNormalizeDays(string value, out string normalized)
    {
        var valid = SalesOrderDefaultDeliveryDatePolicy.TryReadDays(value, out var days);
        normalized = valid ? days.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return valid;
    }

    /// <summary>尽力清理本用例事务，保留原始保存或取消失败。</summary>
    private void Rollback(IDbTransaction transaction)
    {
        try { transaction.Rollback(); }
        catch (Exception exception) { _logger.LogError(exception, "系统参数事务回滚失败"); }
    }
}

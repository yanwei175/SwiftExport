using System.Net;
using Microsoft.Extensions.Logging;
using Refit;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>保留服务端结构化错误；传输失败保守视为结果未知，不依赖通用 Runner 的纯文本映射。</summary>
public sealed class OutboundPersistenceService(IOutboundManagerApi api, ILogger<OutboundPersistenceService> logger) : IOutboundPersistenceService
{
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, OutboundReversePreviewRequest request, CancellationToken token)
        => RunAsync(() => api.PreviewReverseAsync(uid, request, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, ReverseOutboundCommand command, CancellationToken token)
        => RunAsync(() => api.ReverseAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token)
        => RunAsync(() => api.PreviewPostAsync(uid, request, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token)
        => RunAsync(() => api.PostAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.CreateAsync(command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.UpdateAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.DeleteAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token)
        => RunAsync(() => api.GetAsync(uid, token));

    /// <summary>HTTP 权限拒绝可确定本次未执行；其他无法证明的响应保留原命令，不猜测业务结果。</summary>
    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> call)
    {
        try
        {
            var result = await call();
            if (result is null || (result.Success && result.Data is null))
                return ServiceResult<T>.Fail(OutboundErrorCodes.OutcomeUnknown, "服务端响应不完整，请以原命令重试确认结果。");
            if (!result.Success && string.IsNullOrWhiteSpace(result.Error?.Code))
                return ServiceResult<T>.Fail(OutboundErrorCodes.OutcomeUnknown, result.Message ?? "操作结果未知，请保留原命令重试。");
            return result;
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning("出库 API 权限拒绝，HTTP {Status}", (int)ex.StatusCode);
            return ex.StatusCode == HttpStatusCode.Unauthorized
                ? ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录已失效，请重新登录后重试。")
                : ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号无权执行此操作。");
        }
        catch (Exception ex)
        {
            // 不输出请求 DTO、删除原因或服务器异常正文，避免泄露业务数据。
            logger.LogWarning("出库 API 未能确认结果，异常类型 {ExceptionType}", ex.GetType().Name);
            return ServiceResult<T>.Fail(OutboundErrorCodes.OutcomeUnknown, "连接、超时或响应异常，结果未确认；请选择该单并点击刷新，以原命令重试。");
        }
    }
}

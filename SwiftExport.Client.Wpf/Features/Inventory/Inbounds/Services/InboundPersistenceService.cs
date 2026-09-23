using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExportClient.Services.Api;
using Microsoft.Extensions.Logging;
using Refit;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>保留服务端结构化错误；传输失败保守视为结果未知，复用公共非 2xx 错误读取器。</summary>
public sealed class InboundPersistenceService(IInboundManagerApi api, ILogger<InboundPersistenceService> logger) : IInboundPersistenceService
{
    /// <inheritdoc/>
    public Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token)
        => RunAsync(() => api.PreviewReverseAsync(uid, request, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token)
        => RunAsync(() => api.ReverseAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token)
        => RunAsync(() => api.PreviewPostAsync(uid, request, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token)
        => RunAsync(() => api.PostAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.CreateAsync(command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.UpdateAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token)
        => RunAsync(() => api.DeleteAsync(uid, command, token));
    /// <inheritdoc/>
    public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token)
        => RunAsync(() => api.GetAsync(uid, token));

    /// <summary>HTTP 权限拒绝可确定本次未执行；其他无法证明的响应保留原命令，不猜测业务结果。</summary>
    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> call)
    {
        try
        {
            var result = await call();
            if (result is null || (result.Success && result.Data is null))
                return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, "服务端响应不完整，请以原命令重试确认结果。");
            if (!result.Success && string.IsNullOrWhiteSpace(result.Error?.Code))
                return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, result.Message ?? "操作结果未知，请保留原命令重试。");
            return result;
        }
        catch (ApiException ex)
        {
            logger.LogWarning("单据 API 返回 HTTP {Status}", (int)ex.StatusCode);
            return ApiErrorResponseReader.Read<T>(ex, "单据请求失败");
        }
        catch (Exception ex)
        {
            // 不输出请求 DTO、删除原因或服务器异常正文，避免泄露业务数据。
            logger.LogWarning("入库 API 未能确认结果，异常类型 {ExceptionType}", ex.GetType().Name);
            return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, "连接、超时或响应异常，结果未确认；请选择该单并点击刷新，以原命令重试。");
        }
    }
}

using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>出库持久化边界，统一命令与详情错误语义，不持有页面或弹窗。</summary>
public interface IOutboundPersistenceService
{
    /// <summary>读取当前单据过账预览，不产生写入。</summary>
    Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token);
    /// <summary>提交冻结的正向过账命令。</summary>
    Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token);
    /// <summary>提交已经冻结的新建命令。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token);
    /// <summary>按原整单版本提交已经冻结的更新命令。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token);
    /// <summary>提交已经冻结的软删除命令。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token);
    /// <summary>写成功后读取最新详情；失败不得解释为写入失败。</summary>
    Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token);
}

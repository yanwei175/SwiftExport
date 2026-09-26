using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>入库持久化边界，统一命令与详情错误语义，不持有页面或弹窗。</summary>
public interface IInboundPersistenceService
{
    /// <summary>读取整单撤销预览，不写入库存。</summary>
    Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token);
    /// <summary>提交冻结的整单反过账命令。</summary>
    Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token);
    /// <summary>读取当前单据过账预览，不产生写入。</summary>
    Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token);
    /// <summary>提交冻结的正向过账命令。</summary>
    Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token);
    /// <summary>提交已经冻结的新建命令。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token);
    /// <summary>按原整单版本提交已经冻结的更新命令。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token);
    /// <summary>提交已经冻结的软删除命令。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token);
    /// <summary>写成功后读取最新详情；失败不得解释为写入失败。</summary>
    Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token);
}

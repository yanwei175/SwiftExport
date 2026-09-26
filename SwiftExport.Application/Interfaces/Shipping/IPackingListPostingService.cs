using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

/// <summary>箱单过账 HTTP 用例边界，不接受客户端库存分配及操作人。</summary>
public interface IPackingListPostingService
{
    /// <summary>只读计算并补充预览展示字段。</summary>
    Task<ServiceResult<PackingListPostingPreviewDto>> PreviewAsync(Guid uid, PackingListPostPreviewRequest request, CancellationToken token);
    /// <summary>按令牌和稳定命令执行一次原子过账。</summary>
    Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, PostPackingListCommand request, CancellationToken token);
    /// <summary>根据原执行事实预览整单反向。</summary>
    Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReversePreviewRequest request, CancellationToken token);
    /// <summary>整单反向；正常路径不允许负库存。</summary>
    Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, ReversePackingListCommand request, CancellationToken token);
}

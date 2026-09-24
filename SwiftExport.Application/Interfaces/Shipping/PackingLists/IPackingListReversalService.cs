using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping.PackingLists;

/// <summary>提供箱单整单反向预览及正式反向命令。</summary>
public interface IPackingListReversalService
{
    /// <summary>读取原执行事实并返回整单反向影响，不写入库存。</summary>
    Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReversePreviewRequest request, CancellationToken token);
    /// <summary>独占外层事务，先反向出库再反向本箱单自动入库。</summary>
    Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, ReversePackingListCommand request, CancellationToken token);
}

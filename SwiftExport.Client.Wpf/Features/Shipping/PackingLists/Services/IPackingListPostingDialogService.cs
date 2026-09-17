using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

/// <summary>过账输入、只读预览和执行追踪的 UI 边界，不负责提交库存命令。</summary>
public interface IPackingListPostingDialogService
{
    /// <summary>获取库存日期和反向原因，取消返回空。</summary>
    Task<PackingListPostingOptions?> GetOptionsAsync(bool reverse, CancellationToken token);
    /// <summary>展示完整预览；只有明确确认且无阻断时返回真。</summary>
    Task<bool> ConfirmAsync(PackingListPostingPreviewDto preview, CancellationToken token);
    /// <summary>展示原始执行证据，不能编辑。</summary>
    Task ShowTraceAsync(PackingListTraceDto trace, CancellationToken token);
}

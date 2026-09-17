using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

/// <summary>组装过账弹窗视图，不访问数据库或发出库存命令。</summary>
public sealed class PackingListPostingDialogService(IDialogService dialogs) : IPackingListPostingDialogService
{
    /// <inheritdoc/>
    public async Task<PackingListPostingOptions?> GetOptionsAsync(bool reverse, CancellationToken token)
    {
        var model = new PackingListPostingOptions { IsReverse = reverse };
        var result = await dialogs.ShowAsync(new PackingListPostingOptionsView { DataContext = model },
            new DialogOptions(reverse ? "反过账日期与原因" : "过账业务日期") { PrimaryButtonText = "生成预览", CloseButtonText = "取消" }, cancellationToken: token);
        return result == DialogResult.Primary ? model : null;
    }
    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(PackingListPostingPreviewDto preview, CancellationToken token)
    {
        if (preview.Blockers.Count > 0 || string.IsNullOrWhiteSpace(preview.PreviewToken))
        {
            await dialogs.ShowContentAsync("预览被阻断", new PackingListPostingPreviewView { DataContext = preview }, cancellationToken: token);
            return false;
        }
        return await dialogs.ShowAsync(new PackingListPostingPreviewView { DataContext = preview },
            new DialogOptions(preview.IsReverse ? "确认整单反过账：先恢复出库，再撤销自动入库" : "确认过账：执行以下入出库及交付记录")
            { PrimaryButtonText = "确认执行", CloseButtonText = "取消", WidthRatio = .9, HeightRatio = .8,
                ScrollStrategy = DialogScrollStrategy.Content }, cancellationToken: token) == DialogResult.Primary;
    }
    /// <inheritdoc/>
    public Task ShowTraceAsync(PackingListTraceDto trace, CancellationToken token) =>
        dialogs.ShowContentAsync("PackingList 执行追踪", new PackingListTraceView { DataContext = trace }, cancellationToken: token);
}

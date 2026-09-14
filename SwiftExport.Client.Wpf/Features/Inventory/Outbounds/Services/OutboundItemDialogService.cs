using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>为每次打开创建独立选源会话，仅在确认时接纳快照并在关闭后释放请求。</summary>
public sealed class OutboundItemDialogService(IDialogService dialogs, IOutboundManagerApi api, IApiCallRunner runner) : IOutboundItemDialogService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboundItemCandidateDto>?> PickAsync(OutboundItemPickerRequest request, CancellationToken cancellationToken = default)
    {
        using var picker = new OutboundItemPickerViewModel(api, runner);
        await picker.InitializeAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(new OutboundItemPickerView { DataContext = picker },
            new DialogOptions("选择出库来源")
            {
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                WidthRatio = 0.8,
                HeightRatio = 0.7,
                ScrollStrategy = DialogScrollStrategy.Content
            }, picker, cancellationToken);
        return result == DialogResult.Primary && picker.IsPrimaryButtonEnabled ? picker.GetSelectedResult() : null;
    }
}

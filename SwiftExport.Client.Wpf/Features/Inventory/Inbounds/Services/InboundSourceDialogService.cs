using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>为每次打开创建独立选源会话，仅在确认时接纳快照并在关闭后释放请求。</summary>
public sealed class InboundSourceDialogService(IDialogService dialogs, IInboundSourceApi api, IApiCallRunner runner, IReferenceDataCacheService cache) : IInboundSourceDialogService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundSourceCandidateDto>?> PickAsync(InboundSourcePickerRequest request, CancellationToken cancellationToken = default)
    {
        using var picker = new InboundSourcePickerViewModel(api, runner, cache);
        await picker.InitializeAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(new InboundSourcePickerView { DataContext = picker },
            new DialogOptions("选择入库来源")
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

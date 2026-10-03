using SwiftExport.Contracts.Apis.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

/// <summary>拥有选源弹窗及其会话生命周期，取消不会修改调用方草稿。</summary>
public sealed class PackingListSourcePickerService(IPackingListApi api, IApiCallRunner runner, IDialogService dialogs)
    : IPackingListSourcePickerService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PackingListSourcePickerSelection>?> AllocateAsync(Guid customerId, IReadOnlyDictionary<Guid, decimal> draftQuantities,
        Guid? productId, bool nonStandard, string? invoices, decimal limit, IReadOnlyList<PackingListSourcePickerSelection>? previous, CancellationToken token)
    {
        using var picker = new PackingListSourcePickerViewModel(api, runner);
        picker.ConfigureAllocation(productId, nonStandard, invoices, limit);
        await picker.InitializeAsync(customerId, draftQuantities, token);
        picker.RestoreAllocations(previous);
        token.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(new PackingListSourcePickerView { DataContext = picker },
            new DialogOptions("分配待关联行的来源数量") { PrimaryButtonText = "关联并拆行", CloseButtonText = "取消", WidthRatio = 0.85, HeightRatio = 0.75, ScrollStrategy = DialogScrollStrategy.Content }, picker, token);
        return result == DialogResult.Primary ? picker.GetSelectedResult() : null;
    }
    /// <inheritdoc/>
    public async Task<IReadOnlyList<PackingListSourcePickerSelection>?> PickAsync(Guid customerId,
        IReadOnlyDictionary<Guid, decimal> draftQuantities, CancellationToken token)
    {
        using var picker = new PackingListSourcePickerViewModel(api, runner);
        await picker.InitializeAsync(customerId, draftQuantities, token);
        token.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(new PackingListSourcePickerView { DataContext = picker },
            new DialogOptions("选择待交付来源") { PrimaryButtonText = "加入箱单", CloseButtonText = "取消",
                WidthRatio = 0.85, HeightRatio = 0.75, ScrollStrategy = DialogScrollStrategy.Content }, picker, token);
        return result == DialogResult.Primary ? picker.GetSelectedResult() : null;
    }
}

using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Bom.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.Services;

/// <summary>通过 Shell 统一 Dialog 宿主组装 BOM 交互，弹窗对象在关闭后释放。</summary>
public sealed class BomManagementDialogService(IDialogService dialogs, IBomBindingClientService binding,
    IBomApi api, IApiCallRunner runner) : IBomManagementDialogService
{
    /// <inheritdoc />
    public async Task<BomBindingDialogResult> BindAsync(BomProductItemModel product, CancellationToken cancellationToken)
    {
        using var model = new BomBindingDialogViewModel(binding, product);
        var reason = await dialogs.ShowAsync(new BomBindingDialogView { DataContext = model }, new DialogOptions("建立 / 绑定 BOM")
        { Width = 560, PrimaryButtonText = "保存绑定", PrimaryButtonCommand = model.SaveCommand, CloseButtonText = "取消" }, model, cancellationToken);
        return new(reason, reason == DialogResult.Primary ? model.Saved : null, model.BomNo, model.OutcomeUnknown);
    }
    /// <inheritdoc />
    public async Task<BomSourceSelectionResult> SelectSourceAsync(Guid targetId, CancellationToken cancellationToken)
    {
        using var model = new BomSourcePickerViewModel(api, runner, targetId, cancellationToken);
        await model.LoadAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var reason = await dialogs.ShowAsync(new BomSourcePickerView { DataContext = model }, new DialogOptions("复制 BOM · 选择来源成品")
        { Width = 880, Height = 600, PrimaryButtonText = "复制到当前空 BOM", CloseButtonText = "取消", ScrollStrategy = DialogScrollStrategy.Content }, model, cancellationToken);
        return new(reason, reason == DialogResult.Primary ? model.SelectedProduct : null);
    }
    /// <inheritdoc />
    public Task ShowTreeAsync(string sku, IReadOnlyList<BomTreeDto> nodes, CancellationToken cancellationToken) =>
        dialogs.ShowAsync(new BomTreeView { DataContext = nodes }, new DialogOptions($"BOM 结构 · {sku}")
        { Width = 900, Height = 640, CloseButtonText = "关闭", ScrollStrategy = DialogScrollStrategy.Content }, cancellationToken: cancellationToken);
}

using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Services;

/// <summary>为每次选择创建独立视图模型，并在公共弹窗边界接纳结果和释放会话。</summary>
public sealed class WarehousePickerService(Func<WarehousePickerViewModel> createPicker, IDialogService dialogs) : IWarehousePickerService
{
    /// <inheritdoc/>
    public async Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default)
    {
        using var picker = createPicker();
        await picker.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(
            new WarehousePickerView { DataContext = picker },
            new DialogOptions("选择仓库")
            {
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = DialogButton.Primary,
                WidthRatio = 0.9,
                HeightRatio = 0.8,
                ScrollStrategy = DialogScrollStrategy.Content
            },
            picker,
            cancellationToken);
        return result == DialogResult.Primary ? picker.GetSelectedResult() : null;
    }
}

using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Services;

/// <summary>创建仓库选择器视图，并在公共弹窗边界接纳确认结果和释放查询会话。</summary>
public sealed class WarehousePickerService(WarehousePickerViewModel picker, IDialogService dialogs) : IWarehousePickerService
{
    private int _activeSession;

    /// <inheritdoc/>
    public async Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _activeSession, 1, 0) != 0)
            throw new InvalidOperationException("仓库选择器已经打开，不能重复进入。");
        try
        {
            await picker.OpenAsync(cancellationToken);
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
        finally
        {
            picker.Close();
            Interlocked.Exchange(ref _activeSession, 0);
        }
    }
}

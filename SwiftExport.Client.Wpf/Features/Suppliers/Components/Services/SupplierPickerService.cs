using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Suppliers.Components.View;
using SwiftExportClient.Features.Suppliers.Components.ViewModel;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Suppliers.Components.Services;

/// <summary>为每次供应商选择创建独立会话，并在公共 Dialog 边界接纳单选结果。</summary>
public sealed class SupplierPickerService(
    Func<SupplierPickerViewModel> createPicker,
    IDialogService dialogs) : ISupplierPickerService
{
    /// <inheritdoc/>
    public async Task<SupplierDto?> PickAsync(CancellationToken cancellationToken = default)
    {
        using var picker = createPicker();
        await picker.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await dialogs.ShowAsync(
            new SupplierPickerView { DataContext = picker },
            new DialogOptions("选择供应商")
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

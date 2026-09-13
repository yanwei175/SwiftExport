using SwiftExport.Contracts.DTOs.Inventory;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Services;

/// <summary>使用 WPF-UI 对话框确认虚拟仓目标并采集原因，不负责调用业务 API。</summary>
public sealed class WarehouseSiteReasonDialogService(IContentDialogService dialogs) : IWarehouseSiteReasonDialogService
{
    /// <inheritdoc/>
    public async Task<string?> RequestAsync(string action, WarehouseSiteDto warehouse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(warehouse);
        var reasonBox = new TextBox
        {
            AcceptsReturn = true, MaxLength = WarehouseSiteLimits.ReasonMaxLength, MinWidth = 440, MinHeight = 96,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(reasonBox, action + "原因");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"目标：{warehouse.Code} · {warehouse.Name}\n该操作会由服务端重新校验版本和业务引用，请填写真实{action}原因。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(reasonBox);
        var dialog = new ContentDialog
        {
            Title = action + "供应商虚拟仓", Content = panel, PrimaryButtonText = action,
            CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false
        };
        void OnReasonChanged(object sender, TextChangedEventArgs args) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);
        reasonBox.TextChanged += OnReasonChanged;
        try
        {
            var result = await dialogs.ShowAsync(dialog, cancellationToken);
            return result == ContentDialogResult.Primary ? reasonBox.Text.Trim() : null;
        }
        finally
        {
            reasonBox.TextChanged -= OnReasonChanged;
        }
    }
}

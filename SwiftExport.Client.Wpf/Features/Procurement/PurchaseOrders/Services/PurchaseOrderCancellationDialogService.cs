using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>使用 WPF-UI ContentDialog 实现采购取消原因采集，并在本地阻止空原因提交。</summary>
public sealed class PurchaseOrderCancellationDialogService(IContentDialogService contentDialogService)
    : IPurchaseOrderCancellationDialogService
{
    /// <inheritdoc/>
    public async Task<string?> RequestReasonAsync(string orderNo, CancellationToken cancellationToken = default)
    {
        var reasonBox = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            MaxLength = 1000,
            MinWidth = 420,
            MinHeight = 88,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var content = new StackPanel();
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "取消确认可能被已存在的收货等下游事实阻断。请输入必填取消原因，最终资格由服务端校验。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(reasonBox);

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrWhiteSpace(orderNo) ? "取消采购确认" : $"取消确认 · {orderNo}",
            Content = content,
            PrimaryButtonText = "取消确认",
            CloseButtonText = "返回",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        void OnReasonChanged(object sender, TextChangedEventArgs args)
            => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);

        reasonBox.TextChanged += OnReasonChanged;
        try
        {
            var result = await contentDialogService.ShowAsync(dialog, cancellationToken);
            return result == ContentDialogResult.Primary ? reasonBox.Text.Trim() : null;
        }
        finally
        {
            reasonBox.TextChanged -= OnReasonChanged;
        }
    }
}

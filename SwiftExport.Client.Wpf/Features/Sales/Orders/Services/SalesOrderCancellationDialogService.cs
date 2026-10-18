using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Sales.Orders.Services;

public sealed class SalesOrderCancellationDialogService(
    IContentDialogService contentDialogService) : ISalesOrderCancellationDialogService
{
    public async Task<string?> RequestReasonAsync(
        string orderNo,
        CancellationToken cancellationToken = default)
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
            Text = "从未拆解的成品需求将直接删除；已经拆解并安全回退的历史将作废保留。请输入取消原因。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(reasonBox);

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrWhiteSpace(orderNo) ? "取消销售订单确认" : $"取消确认 · {orderNo}",
            Content = content,
            PrimaryButtonText = "取消确认",
            CloseButtonText = "返回",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        void OnReasonChanged(object sender, TextChangedEventArgs args)
            => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text)
                                               && reasonBox.Text.Trim().Length <= 1000;
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

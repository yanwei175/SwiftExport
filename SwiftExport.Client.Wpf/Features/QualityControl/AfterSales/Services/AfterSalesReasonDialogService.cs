using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using ContentDialog = Wpf.Ui.Controls.ContentDialog;
using ContentDialogButton = Wpf.Ui.Controls.ContentDialogButton;
using ContentDialogResult = Wpf.Ui.Controls.ContentDialogResult;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>使用 WPF-UI 对话框采集售后操作原因，不提交业务命令。</summary>
public sealed class AfterSalesReasonDialogService(IContentDialogService dialogs) : IAfterSalesReasonDialogService
{
    /// <inheritdoc/>
    public async Task<string?> RequestAsync(string title, string guidance, CancellationToken cancellationToken = default)
    {
        var box = new TextBox
        {
            AcceptsReturn = true, MaxLength = 2000, MinWidth = 440, MinHeight = 96,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        System.Windows.Automation.AutomationProperties.SetName(box, title + "原因");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(box);
        var dialog = new ContentDialog
        {
            Title = title, Content = panel, PrimaryButtonText = "确认", CloseButtonText = "返回",
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false
        };
        void Changed(object sender, TextChangedEventArgs args) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(box.Text);
        box.TextChanged += Changed;
        try
        {
            return await dialogs.ShowAsync(dialog, cancellationToken) == ContentDialogResult.Primary ? box.Text.Trim() : null;
        }
        finally
        {
            box.TextChanged -= Changed;
        }
    }
}

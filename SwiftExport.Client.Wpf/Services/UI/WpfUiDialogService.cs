using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SwiftExportClient.Services.UI;

public sealed class WpfUiDialogService(IContentDialogService contentDialogService) : IDialogService
{
    public async Task ShowContentAsync(
        string title,
        object content,
        string closeButtonText = "关闭",
        CancellationToken cancellationToken = default)
    {
        await contentDialogService.ShowAsync(
            new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Close
            },
            cancellationToken);
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string closeButtonText = "取消",
        CancellationToken cancellationToken = default)
    {
        var result = await contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Close
            },
            cancellationToken);

        return result == ContentDialogResult.Primary;
    }

    public Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        return contentDialogService.ShowAlertAsync(title, message, "关闭", cancellationToken);
    }
}

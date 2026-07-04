using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Services.UI;

public sealed class WpfUiMessageService(
    ISnackbarService snackbarService,
    IStatusMessageService statusMessageService)
    : IMessageService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public void ShowInfo(string title, string message)
    {
        Show(title, message, UiMessageSeverity.Info, ControlAppearance.Info);
    }

    public void ShowSuccess(string title, string message)
    {
        Show(title, message, UiMessageSeverity.Success, ControlAppearance.Success);
    }

    public void ShowWarning(string title, string message)
    {
        Show(title, message, UiMessageSeverity.Warning, ControlAppearance.Caution);
    }

    public void ShowError(string title, string message)
    {
        Show(title, message, UiMessageSeverity.Error, ControlAppearance.Danger);
    }

    private void Show(
        string title,
        string message,
        UiMessageSeverity severity,
        ControlAppearance appearance)
    {
        statusMessageService.Show(title, message, severity);
        snackbarService.Show(title, message, appearance, new SymbolIcon(SymbolRegular.Info24), DefaultTimeout);
    }
}

namespace SwiftExportClient.Services.UI;

/// <summary>把 Feature 消息限制在当前模态窗口，避免依赖被禁用的 Shell Snackbar。</summary>
public sealed class WindowMessageService : IMessageService
{
    /// <summary>窗口可订阅此事件并选择合适的本地消息控件显示。</summary>
    public event EventHandler<StatusMessageChangedEventArgs>? MessageChanged;

    /// <inheritdoc />
    public void ShowInfo(string title, string message)
        => Show(title, message, UiMessageSeverity.Info);

    /// <inheritdoc />
    public void ShowSuccess(string title, string message)
        => Show(title, message, UiMessageSeverity.Success);

    /// <inheritdoc />
    public void ShowWarning(string title, string message)
        => Show(title, message, UiMessageSeverity.Warning);

    /// <inheritdoc />
    public void ShowError(string title, string message)
        => Show(title, message, UiMessageSeverity.Error);

    private void Show(string title, string message, UiMessageSeverity severity)
    {
        MessageChanged?.Invoke(
            this,
            new StatusMessageChangedEventArgs(title, message, severity, isOpen: true));
    }
}

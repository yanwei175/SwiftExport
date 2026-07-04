namespace SwiftExportClient.Services.UI;

public sealed class StatusMessageChangedEventArgs(
    string? title,
    string? message,
    UiMessageSeverity severity,
    bool isOpen)
    : EventArgs
{
    public string? Title { get; } = title;

    public string? Message { get; } = message;

    public UiMessageSeverity Severity { get; } = severity;

    public bool IsOpen { get; } = isOpen;
}

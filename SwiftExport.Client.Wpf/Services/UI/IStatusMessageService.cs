namespace SwiftExportClient.Services.UI;

public interface IStatusMessageService
{
    event EventHandler<StatusMessageChangedEventArgs>? MessageChanged;

    string? Title { get; }

    string? Message { get; }

    UiMessageSeverity Severity { get; }

    bool IsOpen { get; }

    void Show(string title, string message, UiMessageSeverity severity);

    void Clear();
}

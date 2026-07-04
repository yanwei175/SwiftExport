namespace SwiftExportClient.Services.UI;

public sealed class StatusMessageService : IStatusMessageService
{
    public event EventHandler<StatusMessageChangedEventArgs>? MessageChanged;

    public string? Title { get; private set; }

    public string? Message { get; private set; }

    public UiMessageSeverity Severity { get; private set; } = UiMessageSeverity.Info;

    public bool IsOpen { get; private set; }

    public void Show(string title, string message, UiMessageSeverity severity)
    {
        Title = title;
        Message = message;
        Severity = severity;
        IsOpen = true;
        RaiseChanged();
    }

    public void Clear()
    {
        Title = null;
        Message = null;
        Severity = UiMessageSeverity.Info;
        IsOpen = false;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        MessageChanged?.Invoke(this, new StatusMessageChangedEventArgs(Title, Message, Severity, IsOpen));
    }
}

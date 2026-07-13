using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SwiftExportClient.Mvvm;

public abstract partial class ViewModelBaseEx : ObservableObjectEx
{
    private readonly List<IRelayCommand> _busyAwareCommands = [];
    private int _busyCount;

    [ObservableProperty]
    public partial string Title { get; protected set; } = string.Empty;

    [ObservableProperty]
    public partial string? BusyMessage { get; protected set; }

    [ObservableProperty]
    public partial string? ValidationSummary { get; protected set; }

    [ObservableProperty]
    public partial bool IsDirty { get; protected set; }

    public bool IsBusy => _busyCount > 0;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    public partial bool IsReadOnly { get; protected set; }

    public bool CanWrite => !IsReadOnly;

    partial void OnIsReadOnlyChanged(bool value) => NotifyRegisteredCommands();

    protected void RegisterBusyAwareCommand(IRelayCommand command)
    {
        if (!_busyAwareCommands.Contains(command))
        {
            _busyAwareCommands.Add(command);
        }
    }

    protected async Task RunBusyAsync(
        Func<Task> action,
        string? busyMessage = null,
        bool skipIfBusy = true)
    {
        if (skipIfBusy && IsBusy)
        {
            return;
        }

        var previousBusyMessage = BusyMessage;
        BeginBusy(busyMessage);

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // 用户取消不属于页面错误；调用方如需提示，应在具体命令中表达。
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex);
        }
        finally
        {
            EndBusy(previousBusyMessage);
        }
    }

    protected Task RunWriteAsync(
        Func<Task> action,
        string? busyMessage = null,
        bool skipIfBusy = true)
    {
        return IsReadOnly
            ? HandleReadOnlyViolationAsync()
            : RunBusyAsync(action, busyMessage, skipIfBusy);
    }

    protected virtual Task HandleExceptionAsync(Exception exception)
    {
        ValidationSummary = exception.Message;
        return Task.CompletedTask;
    }

    protected virtual Task HandleReadOnlyViolationAsync()
    {
        ValidationSummary = "当前页面为只读状态，不能执行写入操作。";
        return Task.CompletedTask;
    }

    protected virtual void AcceptChanges()
    {
        IsDirty = false;
        ValidationSummary = null;
    }

    protected virtual void RejectChanges()
    {
        IsDirty = false;
        ValidationSummary = null;
    }

    private void BeginBusy(string? busyMessage)
    {
        Interlocked.Increment(ref _busyCount);
        BusyMessage = busyMessage;
        RaiseBusyStateChanged();
    }

    private void EndBusy(string? previousBusyMessage)
    {
        Interlocked.Decrement(ref _busyCount);
        BusyMessage = previousBusyMessage;
        RaiseBusyStateChanged();
    }

    private void RaiseBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsNotBusy));
        NotifyRegisteredCommands();
    }

    private void NotifyRegisteredCommands()
    {
        foreach (var command in _busyAwareCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }
}

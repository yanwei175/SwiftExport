using CommunityToolkit.Mvvm.Input;

namespace SwiftExportClient.Mvvm;

public abstract class ViewModelBaseEx : ObservableObjectEx
{
    private readonly List<IRelayCommand> _busyAwareCommands = [];
    private int _busyCount;
    private string _title = string.Empty;
    private string? _busyMessage;
    private string? _validationSummary;
    private bool _isDirty;
    private bool _isReadOnly;

    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    public string? BusyMessage
    {
        get => _busyMessage;
        protected set => SetProperty(ref _busyMessage, value);
    }

    public string? ValidationSummary
    {
        get => _validationSummary;
        protected set => SetProperty(ref _validationSummary, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        protected set => SetProperty(ref _isDirty, value);
    }

    public bool IsBusy => _busyCount > 0;

    public bool IsNotBusy => !IsBusy;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        protected set
        {
            if (SetProperty(ref _isReadOnly, value))
            {
                OnPropertyChanged(nameof(CanWrite));
                NotifyRegisteredCommands();
            }
        }
    }

    public bool CanWrite => !IsReadOnly;

    protected void RegisterBusyAwareCommand(IRelayCommand command)
    {
        _busyAwareCommands.Add(command);
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

using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Mvvm;

public abstract class ObservableObjectEx : ObservableObject
{
    private int _suppressCount;

    public bool IsSuppressed => Volatile.Read(ref _suppressCount) > 0;

    protected IDisposable SuppressScope()
    {
        if (Interlocked.Increment(ref _suppressCount) == 1)
        {
            OnPropertyChanged(nameof(IsSuppressed));
        }

        return new ActionOnDispose(EndSuppress);
    }

    private void EndSuppress()
    {
        if (Interlocked.Decrement(ref _suppressCount) == 0)
        {
            OnPropertyChanged(nameof(IsSuppressed));
        }
    }

    private sealed class ActionOnDispose : IDisposable
    {
        private Action? _action;

        public ActionOnDispose(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}

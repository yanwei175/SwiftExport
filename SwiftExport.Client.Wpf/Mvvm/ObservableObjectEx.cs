using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Mvvm;

public abstract class ObservableObjectEx : ObservableObject
{
    private int _suppressCount;

    public bool IsSuppressed => _suppressCount > 0;

    protected IDisposable SuppressScope()
    {
        Interlocked.Increment(ref _suppressCount);
        OnPropertyChanged(nameof(IsSuppressed));

        return new ActionOnDispose(() =>
        {
            Interlocked.Decrement(ref _suppressCount);
            OnPropertyChanged(nameof(IsSuppressed));
        });
    }

    private sealed class ActionOnDispose(Action action) : IDisposable
    {
        public void Dispose()
        {
            action();
        }
    }
}

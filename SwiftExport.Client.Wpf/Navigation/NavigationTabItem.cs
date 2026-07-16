using System.ComponentModel;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Navigation;

public sealed class NavigationTabItem : ObservableObjectEx, IDisposable
{
    private string _documentKey;
    private string _title;
    private bool _isDirty;
    private INotifyPropertyChanged? _documentNotifier;

    public NavigationTabItem(
        NavigationRoute route,
        object contentViewModel,
        string? documentKey = null)
    {
        RouteKey = route.Key;
        _documentKey = string.IsNullOrWhiteSpace(documentKey) ? route.Key : documentKey;
        _title = route.Title;
        CanClose = route.CanClose;
        ContentViewModel = contentViewModel;

        if (contentViewModel is IDocumentTabViewModel document)
        {
            SynchronizeDocumentState(document);
            _documentNotifier = contentViewModel as INotifyPropertyChanged;
            if (_documentNotifier is not null)
            {
                _documentNotifier.PropertyChanged += OnDocumentPropertyChanged;
            }
        }
    }

    public string RouteKey { get; }

    public string DocumentKey
    {
        get => _documentKey;
        internal set => SetProperty(ref _documentKey, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public bool CanClose { get; }

    public object ContentViewModel { get; }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public void Dispose()
    {
        if (_documentNotifier is not null)
        {
            _documentNotifier.PropertyChanged -= OnDocumentPropertyChanged;
            _documentNotifier = null;
        }

        if (ContentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ContentViewModel is not IDocumentTabViewModel document)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(IDocumentTabViewModel.DocumentTitle)
                or nameof(IDocumentTabViewModel.HasUnsavedChanges))
        {
            SynchronizeDocumentState(document);
        }
    }

    private void SynchronizeDocumentState(IDocumentTabViewModel document)
    {
        Title = string.IsNullOrWhiteSpace(document.DocumentTitle)
            ? Title
            : document.DocumentTitle;
        IsDirty = document.HasUnsavedChanges;
    }
}

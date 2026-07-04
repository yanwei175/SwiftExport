using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Navigation;

public sealed class NavigationTabItem : ObservableObjectEx
{
    private bool _isDirty;

    public NavigationTabItem(NavigationRoute route, object contentViewModel)
    {
        RouteKey = route.Key;
        Title = route.Title;
        CanClose = route.CanClose;
        ContentViewModel = contentViewModel;
    }

    public string RouteKey { get; }

    public string Title { get; }

    public bool CanClose { get; }

    public object ContentViewModel { get; }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }
}

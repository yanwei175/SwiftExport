using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Shell;

public sealed class ShellNavigationItem : ObservableObjectEx
{
    private bool _isExpanded;

    public ShellNavigationItem(NavigationMenuItem menuItem)
        : this(
            menuItem.RouteKey,
            menuItem.Title,
            menuItem.Icon,
            menuItem.IsExpanded,
            menuItem.Children.Select(child => new ShellNavigationItem(child)).ToArray())
    {
    }

    private ShellNavigationItem(
        string routeKey,
        string title,
        string? icon,
        bool isExpanded,
        IReadOnlyList<ShellNavigationItem> children)
    {
        RouteKey = routeKey;
        Title = title;
        Icon = icon;
        _isExpanded = isExpanded;
        Children = children;
    }

    public string RouteKey { get; }

    public string Title { get; }

    public string? Icon { get; }

    public bool HasChildren => Children.Count > 0;

    public IReadOnlyList<ShellNavigationItem> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ShellNavigationItem CloneForFilter(IReadOnlyList<ShellNavigationItem> children, bool forceExpanded)
    {
        return new ShellNavigationItem(RouteKey, Title, Icon, forceExpanded || IsExpanded, children);
    }
}

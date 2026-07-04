namespace SwiftExportClient.Navigation;

public sealed record NavigationMenuItem(
    string RouteKey,
    string Title,
    string Group,
    string? Icon = null,
    bool IsExpanded = false,
    IReadOnlyList<NavigationMenuItem>? Children = null)
{
    public IReadOnlyList<NavigationMenuItem> Children { get; } = Children ?? [];
}

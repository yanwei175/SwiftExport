namespace SwiftExportClient.Navigation;

public sealed record NavigationRoute(
    string Key,
    string Title,
    string Group,
    Type ViewModelType,
    bool CanClose = true);

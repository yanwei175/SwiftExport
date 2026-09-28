using System.Collections.ObjectModel;
using SwiftExportClient.Navigation;
namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;
/// <summary>记录设置中心的导航请求，不创建实际业务页面。</summary>
internal sealed class SettingsNavigationSpy : INavigationService
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public string? LastRoute { get; private set; }
    public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
    public NavigationTabItem? SelectedTab { get; set; }
    public Task OpenAsync(string routeKey) { LastRoute = routeKey; return Task.CompletedTask; }
    public Task OpenDocumentAsync(string routeKey, string documentKey, object? parameter = null, CancellationToken cancellationToken = default) => OpenAsync(routeKey);
    public void UpdateDocumentKey(object vm, string documentKey) { }
    public void Activate(NavigationTabItem? tab) => SelectedTab = tab;
    public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
    public void Reset() { }
}

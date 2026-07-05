using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Navigation;

public sealed class NavigationService : ObservableObjectEx, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationRouteRegistry _routeRegistry;
    private readonly IStatusMessageService _statusMessageService;
    private NavigationTabItem? _selectedTab;

    public NavigationService(
        IServiceProvider serviceProvider,
        INavigationRouteRegistry routeRegistry,
        IStatusMessageService statusMessageService)
    {
        _serviceProvider = serviceProvider;
        _routeRegistry = routeRegistry;
        _statusMessageService = statusMessageService;
    }

    public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];

    public NavigationTabItem? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public async Task OpenAsync(string routeKey)
    {
        if (!_routeRegistry.TryGetRoute(routeKey, out var route))
        {
            _statusMessageService.Show("导航", $"页面路由未注册：{routeKey}", UiMessageSeverity.Warning);
            return;
        }

        var existingTab = OpenTabs.FirstOrDefault(tab =>
            string.Equals(tab.RouteKey, route.Key, StringComparison.OrdinalIgnoreCase));

        if (existingTab is not null)
        {
            Activate(existingTab);
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService(route.ViewModelType);
        var tab = new NavigationTabItem(route, viewModel);
        OpenTabs.Add(tab);
        Activate(tab);

        if (viewModel is IAsyncInitializable initializable)
        {
            await initializable.InitializeAsync();
        }
    }

    public void Activate(NavigationTabItem? tab)
    {
        if (tab is null || !OpenTabs.Contains(tab))
        {
            return;
        }

        SelectedTab = tab;
    }

    public Task CloseAsync(NavigationTabItem? tab)
    {
        if (tab is null || !tab.CanClose)
        {
            return Task.CompletedTask;
        }

        var index = OpenTabs.IndexOf(tab);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        OpenTabs.RemoveAt(index);

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = OpenTabs.Count == 0
                ? null
                : OpenTabs[Math.Clamp(index, 0, OpenTabs.Count - 1)];
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        OpenTabs.Clear();
        SelectedTab = null;
    }
}

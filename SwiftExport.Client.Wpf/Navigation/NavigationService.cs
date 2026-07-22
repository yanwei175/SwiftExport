using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

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

    public async Task OpenDocumentAsync(
        string routeKey,
        string documentKey,
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            throw new ArgumentException("文档键不能为空。", nameof(documentKey));
        }

        if (!_routeRegistry.TryGetRoute(routeKey, out var route))
        {
            _statusMessageService.Show("导航", $"页面路由未注册：{routeKey}", UiMessageSeverity.Warning);
            return;
        }

        var existingTab = OpenTabs.FirstOrDefault(tab =>
            string.Equals(tab.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase));
        if (existingTab is not null)
        {
            Activate(existingTab);
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService(route.ViewModelType);
        var tab = new NavigationTabItem(route, viewModel, documentKey);
        OpenTabs.Add(tab);
        Activate(tab);

        if (viewModel is IDocumentTabInitializable initializable)
        {
            await initializable.InitializeDocumentAsync(parameter, cancellationToken);
        }
    }

    public void UpdateDocumentKey(object contentViewModel, string documentKey)
    {
        ArgumentNullException.ThrowIfNull(contentViewModel);
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            throw new ArgumentException("文档键不能为空。", nameof(documentKey));
        }

        var current = OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.ContentViewModel, contentViewModel));
        if (current is null)
        {
            return;
        }

        var duplicate = OpenTabs.FirstOrDefault(tab =>
            !ReferenceEquals(tab, current)
            && string.Equals(tab.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            var currentIndex = OpenTabs.IndexOf(current);
            if (currentIndex >= 0)
            {
                OpenTabs.RemoveAt(currentIndex);
                current.Dispose();
            }

            Activate(duplicate);
            return;
        }

        current.DocumentKey = documentKey;
    }

    public void Activate(NavigationTabItem? tab)
    {
        if (tab is null || !OpenTabs.Contains(tab))
        {
            return;
        }

        SelectedTab = tab;
    }

    public async Task CloseAsync(NavigationTabItem? tab)
    {
        if (tab is null || !tab.CanClose)
        {
            return;
        }

        if (tab.ContentViewModel is IDocumentTabViewModel document
            && !await document.CanCloseAsync(CancellationToken.None))
        {
            return;
        }

        var index = OpenTabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        OpenTabs.RemoveAt(index);
        tab.Dispose();

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = OpenTabs.Count == 0
                ? null
                : OpenTabs[Math.Clamp(index, 0, OpenTabs.Count - 1)];
        }

    }

    public void Reset()
    {
        foreach (var tab in OpenTabs)
        {
            tab.Dispose();
        }

        OpenTabs.Clear();
        SelectedTab = null;
    }
}

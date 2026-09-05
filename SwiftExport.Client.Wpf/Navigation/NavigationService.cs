using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Windows;

namespace SwiftExportClient.Navigation;

/// <summary>管理工作区页签，并在所有打开和激活入口统一执行当前用户的路由权限校验。</summary>
public sealed class NavigationService : ObservableObjectEx, INavigationService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationRouteRegistry _routeRegistry;
    private readonly INavigationAccessService _accessService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStatusMessageService _statusMessageService;
    private NavigationTabItem? _selectedTab;
    private bool _isDisposed;

    public NavigationService(
        IServiceProvider serviceProvider,
        INavigationRouteRegistry routeRegistry,
        INavigationAccessService accessService,
        ICurrentUserService currentUserService,
        IStatusMessageService statusMessageService)
    {
        _serviceProvider = serviceProvider;
        _routeRegistry = routeRegistry;
        _accessService = accessService;
        _currentUserService = currentUserService;
        _statusMessageService = statusMessageService;
        _currentUserService.CurrentUserChanged += OnCurrentUserChanged;
    }

    public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];

    public NavigationTabItem? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is not null && !CanActivate(value, showMessage: true))
            {
                return;
            }

            SetProperty(ref _selectedTab, value);
        }
    }

    public async Task OpenAsync(string routeKey)
    {
        if (!_routeRegistry.TryGetRoute(routeKey, out var route))
        {
            _statusMessageService.Show("导航", $"页面路由未注册：{routeKey}", UiMessageSeverity.Warning);
            return;
        }

        if (!EnsureCanAccess(route))
        {
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

        if (!EnsureCanAccess(route))
        {
            return;
        }

        var existingTab = OpenTabs.FirstOrDefault(tab =>
            string.Equals(tab.RouteKey, route.Key, StringComparison.OrdinalIgnoreCase)
            &&
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
            && string.Equals(tab.RouteKey, current.RouteKey, StringComparison.OrdinalIgnoreCase)
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
        if (tab is null || !CanActivate(tab, showMessage: true))
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
            var fallback = OpenTabs
                .Skip(Math.Clamp(index, 0, OpenTabs.Count))
                .Concat(OpenTabs.Take(Math.Clamp(index, 0, OpenTabs.Count)))
                .FirstOrDefault(candidate => CanActivate(candidate, showMessage: false));
            SelectedTab = fallback;
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

    /// <summary>解除会话变更订阅并释放仍打开的页签。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _currentUserService.CurrentUserChanged -= OnCurrentUserChanged;
        Reset();
    }

    private bool EnsureCanAccess(NavigationRoute route)
    {
        if (_accessService.CanAccess(route))
        {
            return true;
        }

        _statusMessageService.Show("导航", $"当前用户无权访问：{route.Title}", UiMessageSeverity.Warning);
        return false;
    }

    private bool CanActivate(NavigationTabItem tab, bool showMessage)
    {
        if (!OpenTabs.Contains(tab)
            || !_routeRegistry.TryGetRoute(tab.RouteKey, out var route))
        {
            return false;
        }

        if (_accessService.CanAccess(route))
        {
            return true;
        }

        if (showMessage)
        {
            _statusMessageService.Show("导航", $"当前用户无权访问：{route.Title}", UiMessageSeverity.Warning);
        }

        return false;
    }

    /// <summary>身份或权限快照切换后立即清除上一用户创建的页面状态。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Reset);
            return;
        }

        Reset();
    }
}

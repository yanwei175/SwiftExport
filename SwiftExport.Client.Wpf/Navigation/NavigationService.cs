using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace SwiftExportClient.Navigation;

/// <summary>管理工作区页签，并在所有打开和激活入口统一执行当前用户的路由权限校验。</summary>
public sealed class NavigationService : ObservableObjectEx, INavigationService, IDisposable
{
    private const int MaximumBusinessTabCount = 10;
    private const string HomeRouteKey = "home";
    private readonly INavigationTabFactory _tabFactory;
    private readonly INavigationRouteRegistry _routeRegistry;
    private readonly INavigationAccessService _accessService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStatusMessageService _statusMessageService;
    private readonly ILogger<NavigationService> _logger;
    private NavigationTabItem? _selectedTab;
    private string? _sessionUserId;
    private bool _isDisposed;

    /// <summary>创建会话级导航协调器，并订阅当前用户变化以隔离不同会话的页签状态。</summary>
    public NavigationService(
        INavigationTabFactory tabFactory,
        INavigationRouteRegistry routeRegistry,
        INavigationAccessService accessService,
        ICurrentUserService currentUserService,
        IStatusMessageService statusMessageService,
        ILogger<NavigationService> logger)
    {
        _tabFactory = tabFactory;
        _routeRegistry = routeRegistry;
        _accessService = accessService;
        _currentUserService = currentUserService;
        _statusMessageService = statusMessageService;
        _logger = logger;
        _sessionUserId = currentUserService.CurrentUser?.UserId;
        _currentUserService.CurrentUserChanged += OnCurrentUserChanged;
    }

    /// <inheritdoc />
    public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];

    /// <inheritdoc />
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

    /// <inheritdoc />
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

        if (!EnsureCapacity(route))
        {
            return;
        }

        var previousSelection = SelectedTab;
        var tab = RegisterInitializingTab(route, documentKey: null);
        try
        {
            var lease = _tabFactory.Create(route);
            tab.AttachContent(lease);
            if (lease.ViewModel is IAsyncInitializable initializable)
            {
                await initializable.InitializeAsync();
            }

            if (OpenTabs.Contains(tab))
            {
                tab.CompleteInitialization();
            }
        }
        catch (Exception exception)
        {
            var invalidated = !OpenTabs.Contains(tab) || tab.IsLifetimeCancellationRequested;
            RemoveFailedTab(tab, previousSelection);
            ReportInitializationFailure(route, exception, cancellationRequested: false, invalidated: invalidated);
        }
    }

    /// <inheritdoc />
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

        if (!EnsureCapacity(route))
        {
            return;
        }

        var previousSelection = SelectedTab;
        var tab = RegisterInitializingTab(route, documentKey);
        try
        {
            var lease = _tabFactory.Create(route);
            tab.AttachContent(lease);
            if (lease.ViewModel is IDocumentTabInitializable initializable)
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    tab.LifetimeToken);
                await initializable.InitializeDocumentAsync(parameter, linkedCancellation.Token);
                linkedCancellation.Token.ThrowIfCancellationRequested();
            }

            if (OpenTabs.Contains(tab))
            {
                tab.CompleteInitialization();
            }
        }
        catch (Exception exception)
        {
            var callerCancelled = cancellationToken.IsCancellationRequested;
            var invalidated = !OpenTabs.Contains(tab) || tab.IsLifetimeCancellationRequested;
            RemoveFailedTab(tab, previousSelection);
            ReportInitializationFailure(
                route,
                exception,
                callerCancelled,
                invalidated);
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Activate(NavigationTabItem? tab)
    {
        if (tab is null || !CanActivate(tab, showMessage: true))
        {
            return;
        }

        SelectedTab = tab;
    }

    /// <inheritdoc />
    public async Task CloseAsync(NavigationTabItem? tab)
    {
        if (tab is null || !tab.CanClose)
        {
            return;
        }

        if (tab.IsInitializing)
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

    /// <inheritdoc />
    public void Reset()
    {
        var tabs = OpenTabs.ToArray();
        OpenTabs.Clear();
        SelectedTab = null;

        foreach (var tab in tabs)
        {
            tab.Dispose();
        }
    }

    /// <summary>权限与查重完成后检查业务页签名额，首页不占用十个业务名额。</summary>
    private bool EnsureCapacity(NavigationRoute route)
    {
        if (string.Equals(route.Key, HomeRouteKey, StringComparison.OrdinalIgnoreCase)
            || OpenTabs.Count(tab => !string.Equals(tab.RouteKey, HomeRouteKey, StringComparison.OrdinalIgnoreCase)) < MaximumBusinessTabCount)
        {
            return true;
        }

        _statusMessageService.Show("导航", $"最多同时打开 {MaximumBusinessTabCount} 个业务页签，请先关闭不再使用的页面。", UiMessageSeverity.Warning);
        return false;
    }

    /// <summary>在创建任何页签资源前登记占位，阻止同一路由或文档的重入重复创建。</summary>
    private NavigationTabItem RegisterInitializingTab(NavigationRoute route, string? documentKey)
    {
        var tab = new NavigationTabItem(route, documentKey, isInitializing: true);
        OpenTabs.Add(tab);
        Activate(tab);
        return tab;
    }

    /// <summary>初始化失败或取消时移除占位、释放作用域，并恢复仍有效的选择。</summary>
    private void RemoveFailedTab(NavigationTabItem tab, NavigationTabItem? previousSelection)
    {
        var index = OpenTabs.IndexOf(tab);
        if (index >= 0)
        {
            OpenTabs.RemoveAt(index);
        }

        tab.Dispose();
        if (!ReferenceEquals(SelectedTab, tab))
        {
            return;
        }

        if (previousSelection is not null && CanActivate(previousSelection, showMessage: false))
        {
            SelectedTab = previousSelection;
            return;
        }

        SelectedTab = OpenTabs.FirstOrDefault(candidate => CanActivate(candidate, showMessage: false));
    }

    /// <summary>初始化取消时安静收尾；真实故障由导航边界记录并只显示一次稳定消息。</summary>
    private void ReportInitializationFailure(
        NavigationRoute route,
        Exception exception,
        bool cancellationRequested,
        bool invalidated)
    {
        if (invalidated)
        {
            _logger.LogDebug(exception, "已失效页签 {RouteKey} 的初始化在 Reset 后结束。", route.Key);
            return;
        }

        if (exception is OperationCanceledException && cancellationRequested)
        {
            return;
        }

        _logger.LogError(exception, "初始化页签 {RouteKey} 失败。", route.Key);
        _statusMessageService.Show("导航", $"无法打开“{route.Title}”，请稍后重试。", UiMessageSeverity.Error);
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

    /// <summary>账号切换清除全部页签；同一账号刷新仅关闭已撤权的页面。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ReconcileSession);
            return;
        }

        ReconcileSession();
    }

    /// <summary>撤权后阻止旧页签继续展示和加载；含未保存编辑的页签等待用户确认关闭。</summary>
    private void ReconcileSession()
    {
        var currentId = _currentUserService.CurrentUser?.UserId;
        if (currentId is null || currentId != _sessionUserId)
        {
            _sessionUserId = currentId;
            Reset();
            return;
        }

        foreach (var tab in OpenTabs.ToArray())
        {
            if (_routeRegistry.TryGetRoute(tab.RouteKey, out var route) && _accessService.CanAccess(route)) continue;
            if (tab.IsDirty)
            {
                tab.SuspendForRevokedPermission();
                _statusMessageService.Show("导航", $"“{tab.Title}”的读取权限已撤销，未保存编辑仍保留；请确认后关闭页签。", UiMessageSeverity.Warning);
                continue;
            }
            OpenTabs.Remove(tab);
            tab.Dispose();
        }
        if (SelectedTab is not null && !OpenTabs.Contains(SelectedTab)) SelectedTab = null;
        if (SelectedTab is not null && !_accessService.CanAccess(SelectedTab.RouteKey)) SelectedTab = null;
        SelectedTab ??= OpenTabs.FirstOrDefault(tab => _accessService.CanAccess(tab.RouteKey));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Shell;

namespace SwiftExport.Client.Wpf.Tests.Navigation;

/// <summary>验证路由归属、统一导航门禁、菜单裁剪及跨用户状态隔离。</summary>
[TestClass]
public sealed class NavigationAuthorizationTests
{
    /// <summary>28 条现有列表、详情和隐藏路由全部显式归属到阶段一确定的访问权限。</summary>
    [TestMethod]
    public void RouteRegistry_ShouldClassifyEveryRegisteredRoute()
    {
        var registry = new NavigationRouteRegistry();

        Assert.AreEqual(28, registry.Routes.Count);
        foreach (var route in registry.Routes)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(route.ModuleKey), route.Key);
            Assert.AreEqual(GetExpectedPermission(route.ModuleKey), route.RequiredPermission, route.Key);
        }
    }

    /// <summary>无模块权限时，列表和详情路由都在实例化页面前被拒绝。</summary>
    [TestMethod]
    public async Task Open_ShouldRejectUnauthorizedListAndDetailRoutes()
    {
        var routes = new[]
        {
            Route("sales-orders", typeof(TestPage), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
            Route("sales-order-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess)
        };
        var context = CreateContext(routes);

        await context.Navigation.OpenAsync("sales-orders");
        await context.Navigation.OpenDocumentAsync("sales-order-detail", "sales-order:1");

        Assert.AreEqual(0, context.Navigation.OpenTabs.Count);
        Assert.AreEqual(0, context.Provider.CreatedCount);
        Assert.AreEqual(UiMessageSeverity.Warning, context.Status.LastSeverity);
        StringAssert.Contains(context.Status.LastMessage, "无权访问");
    }

    /// <summary>单模块用户可进入本模块详情，跨模块快捷跳转仍按目标路由重新校验。</summary>
    [TestMethod]
    public async Task OpenDocument_ShouldRecheckTargetRouteAcrossModules()
    {
        var routes = new[]
        {
            Route("gross-demands", typeof(TestPage), NavigationModuleKeys.DemandPlanning, PermissionKeys.ModuleDemandPlanningAccess),
            Route("net-demands", typeof(TestDocumentPage), NavigationModuleKeys.DemandPlanning, PermissionKeys.ModuleDemandPlanningAccess),
            Route("sales-order-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess)
        };
        var context = CreateContext(routes, PermissionKeys.ModuleDemandPlanningAccess);

        await context.Navigation.OpenAsync("gross-demands");
        await context.Navigation.OpenDocumentAsync("net-demands", "net-demand:1");
        await context.Navigation.OpenDocumentAsync("sales-order-detail", "sales-order:1");

        Assert.AreEqual(2, context.Navigation.OpenTabs.Count);
        Assert.AreEqual("net-demands", context.Navigation.SelectedTab?.RouteKey);
    }

    /// <summary>同一文档键只在同一路由内去重，不能把跨模块目标错误激活为其他页面。</summary>
    [TestMethod]
    public async Task OpenDocument_ShouldScopeDeduplicationToRoute()
    {
        var routes = new[]
        {
            Route("sales-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
            Route("shipping-detail", typeof(TestDocumentPage), NavigationModuleKeys.Shipping, PermissionKeys.ModuleShippingAccess)
        };
        var context = CreateContext(
            routes,
            PermissionKeys.ModuleSalesAccess,
            PermissionKeys.ModuleShippingAccess);

        await context.Navigation.OpenDocumentAsync("sales-detail", "document:1");
        await context.Navigation.OpenDocumentAsync("shipping-detail", "document:1");

        Assert.AreEqual(2, context.Navigation.OpenTabs.Count);
        Assert.AreEqual("shipping-detail", context.Navigation.SelectedTab?.RouteKey);
    }

    /// <summary>用户切换会释放旧页签，setter 和 Activate 都不能恢复无权访问的旧页签。</summary>
    [TestMethod]
    public async Task UserSwitch_ShouldClearTabsAndRejectStaleTabActivation()
    {
        var productRoute = Route(
            "products",
            typeof(DisposablePage),
            NavigationModuleKeys.Products,
            PermissionKeys.ModuleProductsAccess);
        var context = CreateContext([productRoute], PermissionKeys.ModuleProductsAccess);

        await context.Navigation.OpenAsync("products");
        var oldPage = (DisposablePage)context.Navigation.SelectedTab!.ContentViewModel;

        context.CurrentUser.SetCurrentUser(CreateMe(PermissionKeys.ModuleSalesAccess));

        Assert.AreEqual(0, context.Navigation.OpenTabs.Count);
        Assert.IsNull(context.Navigation.SelectedTab);
        Assert.IsTrue(oldPage.IsDisposed);

        var staleTab = new NavigationTabItem(productRoute, new DisposablePage());
        context.Navigation.OpenTabs.Add(staleTab);
        context.Navigation.SelectedTab = staleTab;
        context.Navigation.Activate(staleTab);

        Assert.IsNull(context.Navigation.SelectedTab);
        context.Navigation.Reset();
    }

    /// <summary>Shell 仅显示首页和已授权模块，并在搜索父组时继续排除未授权子项。</summary>
    [TestMethod]
    public void ShellMenuFilter_ShouldHideUnauthorizedAndEmptyGroups()
    {
        var registry = new NavigationRouteRegistry();
        var currentUser = CreateCurrentUser(PermissionKeys.ModuleSalesAccess);
        var filter = new ShellNavigationMenuFilter(new NavigationAccessService(registry, currentUser));
        var items = registry.MenuItems.Select(item => new ShellNavigationItem(item)).ToArray();

        var visible = filter.Filter(items, searchText: null);
        var matchingUnauthorizedParent = filter.Filter(items, "系统设置");

        CollectionAssert.AreEquivalent(
            new[] { NavigationModuleKeys.Home, NavigationModuleKeys.Sales },
            visible.Select(item => item.RouteKey).ToArray());
        Assert.AreEqual(1, visible.Single(item => item.RouteKey == NavigationModuleKeys.Sales).Children.Count);
        Assert.AreEqual(0, matchingUnauthorizedParent.Count);
    }

    /// <summary>已登录用户始终可进入首页，匿名状态不能借无 RequiredPermission 绕过认证。</summary>
    [TestMethod]
    public void NavigationAccess_ShouldKeepHomeForAuthenticatedUserOnly()
    {
        var registry = new NavigationRouteRegistry();
        var currentUser = new CurrentUserService();
        var access = new NavigationAccessService(registry, currentUser);

        Assert.IsFalse(access.CanAccess("home"));

        currentUser.SetCurrentUser(CreateMe());

        Assert.IsTrue(access.CanAccess("home"));
        Assert.IsFalse(access.CanAccess("system-dictionaries"));
        Assert.IsTrue(access.CanAccess("settings-center"));
    }

    private static string? GetExpectedPermission(string moduleKey)
    {
        return moduleKey switch
        {
            NavigationModuleKeys.Home => null,
            NavigationModuleKeys.Products => PermissionKeys.ModuleProductsAccess,
            NavigationModuleKeys.Customers => PermissionKeys.ModuleCustomersAccess,
            NavigationModuleKeys.Suppliers => PermissionKeys.ModuleSuppliersAccess,
            NavigationModuleKeys.Sales => PermissionKeys.ModuleSalesAccess,
            NavigationModuleKeys.DemandPlanning => PermissionKeys.ModuleDemandPlanningAccess,
            NavigationModuleKeys.Procurement => PermissionKeys.ModuleProcurementAccess,
            NavigationModuleKeys.Inventory => PermissionKeys.ModuleInventoryAccess,
            NavigationModuleKeys.Shipping => PermissionKeys.ModuleShippingAccess,
            NavigationModuleKeys.SystemSettings => PermissionKeys.SystemSettingsManage,
            NavigationModuleKeys.SettingsCenter => null,
            _ => throw new AssertFailedException($"未知模块归属：{moduleKey}")
        };
    }

    private static NavigationRoute Route(
        string key,
        Type viewModelType,
        string moduleKey,
        string? requiredPermission)
    {
        return new NavigationRoute(key, key, "Workspace", viewModelType, moduleKey, requiredPermission);
    }

    private static TestContext CreateContext(
        IReadOnlyList<NavigationRoute> routes,
        params string[] permissions)
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(routes);
        var currentUser = CreateCurrentUser(permissions);
        var status = new TestStatusMessageService();
        var navigation = new NavigationService(
            provider,
            registry,
            new NavigationAccessService(registry, currentUser),
            currentUser,
            status);
        return new TestContext(provider, currentUser, status, navigation);
    }

    private static CurrentUserService CreateCurrentUser(params string[] permissions)
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(CreateMe(permissions));
        return service;
    }

    private static MeDto CreateMe(params string[] permissions)
    {
        return new MeDto
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = "test-user",
            Permissions = permissions.ToList()
        };
    }

    private sealed record TestContext(
        TestServiceProvider Provider,
        CurrentUserService CurrentUser,
        TestStatusMessageService Status,
        NavigationService Navigation);

    private sealed class TestRouteRegistry(IReadOnlyList<NavigationRoute> routes) : INavigationRouteRegistry
    {
        public IReadOnlyList<NavigationRoute> Routes => routes;
        public IReadOnlyList<NavigationMenuItem> MenuItems => [];

        public bool TryGetRoute(string routeKey, out NavigationRoute route)
        {
            route = routes.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, routeKey, StringComparison.OrdinalIgnoreCase))!;
            return route is not null;
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public int CreatedCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            CreatedCount++;
            return Activator.CreateInstance(serviceType);
        }
    }

    private sealed class TestPage : IAsyncInitializable
    {
        public Task InitializeAsync() => Task.CompletedTask;
    }

    private sealed class TestDocumentPage : IDocumentTabInitializable
    {
        public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class DisposablePage : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TestStatusMessageService : IStatusMessageService
    {
        public event EventHandler<StatusMessageChangedEventArgs>? MessageChanged
        {
            add { }
            remove { }
        }

        public string? Title => null;
        public string? Message => null;
        public UiMessageSeverity Severity => UiMessageSeverity.Info;
        public bool IsOpen => false;
        public string LastMessage { get; private set; } = string.Empty;
        public UiMessageSeverity LastSeverity { get; private set; }

        public void Show(string title, string message, UiMessageSeverity severity)
        {
            LastMessage = message;
            LastSeverity = severity;
        }

        public void Clear()
        {
        }
    }
}

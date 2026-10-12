using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// <summary>全部列表、Manager、Detail 和隐藏路由均显式归属到确定的访问权限。</summary>
    [TestMethod]
    public void RouteRegistry_ShouldClassifyEveryRegisteredRoute()
    {
        var registry = new NavigationRouteRegistry();
        string?[] expected =
        [
            null,
            "Shipping.PackingList.View", "Shipping.PackingList.View",
            "Products.Product.View", "Products.Master.View", "Products.Master.View", "Products.Neutral.View", "Products.Master.View",
            "Products.Category.View", "Products.Color.View", "Products.Bom.View",
            "Customers.Customer.View", "Customers.PoConfig.View", "Customers.Products.View",
            "Suppliers.Supplier.View", "Suppliers.Products.View",
            "Sales.Order.View", "Sales.Order.View", "Sales.Order.View", "Sales.Order.View", "Sales.Order.View",
            "DemandPlanning.Source.View", "DemandPlanning.Source.View", "DemandPlanning.Material.View", "DemandPlanning.Material.View",
            "Procurement.Order.Read", "Procurement.Order.Edit", "Procurement.Order.Read", "Procurement.Progress.View", "Procurement.Order.Read",
            "QualityControl.AfterSales.View", PermissionKeys.QualityControlFlowView, PermissionKeys.QualityControlFlowView,
            "QualityControl.AfterSales.View", "QualityControl.AfterSales.View",
            "Inventory.Warehouse.View", "Inventory.Balance.View", "Inventory.Inbound.View", "Inventory.Outbound.View",
            "Inventory.Balance.View", "DemandPlanning.Source.View", null
        ];

        Assert.AreEqual(expected.Length, registry.Routes.Count);
        for (var index = 0; index < registry.Routes.Count; index++)
        {
            var route = registry.Routes[index];
            Assert.IsFalse(string.IsNullOrWhiteSpace(route.ModuleKey), route.Key);
            Assert.AreEqual(expected[index], route.RequiredPermission, route.Key);
            if (route.Key == "procurement-demand-association")
                CollectionAssert.AreEqual(new[] { PermissionKeys.ProcurementOrderRead }, route.AnyRequiredPermissions!.ToArray());
            else Assert.IsNull(route.AnyRequiredPermissions);
            Assert.AreEqual(route.Key == "system-settings", route.DiscardOnAccessRevoked);
            if (route.RequiredPermission is not null)
                Assert.IsNotNull(PermissionCatalog.Find(route.RequiredPermission), route.Key);
        }
    }

    /// <summary>任一系统读取权限可以进入；无权限或匿名不能进入。</summary>
    [TestMethod]
    [DataRow("System.Users.View")]
    [DataRow("System.Roles.View")]
    [DataRow("System.Permissions.View")]
    [DataRow("System.Dictionary.View")]
    public void SettingsRoute_ShouldAcceptAnyOneReadPermission(string permission)
    {
        var registry = new NavigationRouteRegistry();
        var user = new CurrentUserService();
        var access = new NavigationAccessService(registry, user);
        Assert.IsFalse(access.CanAccess("system-settings"));
        user.SetCurrentUser(new MeDto { UserId = "test", Permissions = [] });
        Assert.IsTrue(access.CanAccess("system-settings"));
        user.SetCurrentUser(new MeDto { UserId = "test", Permissions = [permission] });
        Assert.IsTrue(access.CanAccess("system-settings"));
    }

    /// <summary>无模块权限时，列表和详情路由都在实例化页面前被拒绝。</summary>
    [TestMethod]
    public async Task Open_ShouldRejectUnauthorizedListAndDetailRoutes()
    {
        var routes = new[]
        {
            Route("sales-orders", typeof(TestPage), NavigationModuleKeys.Sales, "Sales.Order.View"),
            Route("sales-order-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, "Sales.Order.View")
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
            Route("gross-demands", typeof(TestPage), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Source.View"),
            Route("net-demands", typeof(TestDocumentPage), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Material.View"),
            Route("sales-order-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, "Sales.Order.View")
        };
        var context = CreateContext(routes, "DemandPlanning.Source.View", "DemandPlanning.Material.View");

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
            Route("sales-detail", typeof(TestDocumentPage), NavigationModuleKeys.Sales, "Sales.Order.View"),
            Route("shipping-detail", typeof(TestDocumentPage), NavigationModuleKeys.Shipping, "Shipping.PackingList.View")
        };
        var context = CreateContext(
            routes,
            "Sales.Order.View",
            "Shipping.PackingList.View");

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
            "Products.Product.View");
        var context = CreateContext([productRoute], "Products.Product.View");

        await context.Navigation.OpenAsync("products");
        var oldPage = (DisposablePage)context.Navigation.SelectedTab!.ContentViewModel;

        context.CurrentUser.SetCurrentUser(CreateMe("Sales.Order.View"));

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

    /// <summary>同一账号刷新后失去 View 权限，未编辑页签释放且旧页面不能继续展示。</summary>
    [TestMethod]
    public async Task SameUserRevocation_RemovesUnauthorizedCleanTab()
    {
        var route = Route("supplier-page", typeof(DisposablePage), NavigationModuleKeys.Suppliers, "Suppliers.Supplier.View");
        var context = CreateContext([route], "Suppliers.Supplier.View");
        await context.Navigation.OpenAsync(route.Key);
        var page = (DisposablePage)context.Navigation.SelectedTab!.ContentViewModel!;
        var userId = context.CurrentUser.CurrentUser!.UserId;

        context.CurrentUser.SetCurrentUser(new MeDto { UserId = userId, Permissions = [] });

        Assert.HasCount(0, context.Navigation.OpenTabs);
        Assert.IsNull(context.Navigation.SelectedTab);
        Assert.IsTrue(page.IsDisposed);
    }

    /// <summary>撤权时未保存编辑不被静默丢弃，但页签失活且不能继续加载。</summary>
    [TestMethod]
    public async Task SameUserRevocation_SuspendsDirtyTabWithoutDiscardingIt()
    {
        var route = Route("supplier-edit", typeof(DirtyPage), NavigationModuleKeys.Suppliers, "Suppliers.Supplier.View");
        var context = CreateContext([route], "Suppliers.Supplier.View");
        await context.Navigation.OpenAsync(route.Key);
        var tab = context.Navigation.SelectedTab!;
        var page = (DirtyPage)tab.ContentViewModel!;
        var userId = context.CurrentUser.CurrentUser!.UserId;

        context.CurrentUser.SetCurrentUser(new MeDto { UserId = userId, Permissions = [] });

        Assert.HasCount(1, context.Navigation.OpenTabs);
        Assert.IsNull(context.Navigation.SelectedTab);
        Assert.IsFalse(page.IsDisposed);
        StringAssert.Contains(context.Status.LastMessage, "未保存编辑");
        context.Navigation.Activate(tab);
        Assert.IsNull(context.Navigation.SelectedTab);
        context.Navigation.Reset();
    }

    /// <summary>Shell 仅显示首页和已授权模块，并在搜索父组时继续排除未授权子项。</summary>
    [TestMethod]
    public void ShellMenuFilter_ShouldHideUnauthorizedAndEmptyGroups()
    {
        var registry = new NavigationRouteRegistry();
        var currentUser = CreateCurrentUser("Sales.Order.View");
        var filter = new ShellNavigationMenuFilter(new NavigationAccessService(registry, currentUser));
        var items = registry.MenuItems.Select(item => new ShellNavigationItem(item)).ToArray();

        var visible = filter.Filter(items, searchText: null);
        var matchingUnauthorizedParent = filter.Filter(items, "系统设置");

        CollectionAssert.AreEquivalent(
            new[] { NavigationModuleKeys.Home, NavigationModuleKeys.Sales },
            visible.Select(item => item.RouteKey).ToArray());
        // 当前菜单只有销售订单管理；履约查询路由仍存在，但不再作为侧边菜单入口。
        var sales = visible.Single(item => item.RouteKey == NavigationModuleKeys.Sales);
        Assert.AreEqual(1, sales.Children.Count);
        Assert.AreEqual(SwiftExportClient.Features.Sales.Orders.ViewModels.SalesOrderManagerViewModel.RouteKey, sales.Children.Single().RouteKey);
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
        Assert.IsTrue(access.CanAccess("system-settings"));
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
            status,
            NullLogger<NavigationService>.Instance);
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

    private sealed class TestServiceProvider : INavigationTabFactory
    {
        public int CreatedCount { get; private set; }

        public INavigationTabLease Create(NavigationRoute route)
        {
            CreatedCount++;
            return new TestLease(Activator.CreateInstance(route.ViewModelType)!);
        }

        private sealed class TestLease(object viewModel) : INavigationTabLease
        {
            private object? _viewModel = viewModel;

            public object ViewModel => _viewModel ?? throw new ObjectDisposedException(nameof(TestLease));

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _viewModel, null) is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
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

    /// <summary>模拟需要保留未保存编辑的文档页签。</summary>
    private sealed class DirtyPage : IDocumentTabViewModel, IDisposable
    {
        public string DocumentTitle => "供应商编辑";
        public bool HasUnsavedChanges => true;
        public bool IsDisposed { get; private set; }
        public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public void Dispose() => IsDisposed = true;
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

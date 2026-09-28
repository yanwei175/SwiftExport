using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using Microsoft.Extensions.Logging.Abstractions;

namespace SwiftExport.Client.Wpf.Tests.Navigation;

[TestClass]
public sealed class NavigationServiceTests
{
    private const string CategoryRouteKey = "product-category-manager";

    [TestMethod]
    public void RouteRegistry_ShouldRegisterProductCategoryManagerAsTabRoute()
    {
        var registry = new NavigationRouteRegistry();

        Assert.IsTrue(registry.TryGetRoute(CategoryRouteKey, out var route));
        Assert.AreEqual(typeof(ProductCategoryManagerViewModel), route.ViewModelType);
        Assert.AreEqual("产品分类管理", route.Title);
        Assert.IsTrue(route.CanClose);
        Assert.AreEqual(NavigationModuleKeys.Products, route.ModuleKey);
        Assert.AreEqual("Products.Category.View", route.RequiredPermission);
    }

    [TestMethod]
    public void RouteRegistry_ShouldExposeGrossDemandUnderDemandPlanningAndKeepLegacyRouteHidden()
    {
        var registry = new NavigationRouteRegistry();

        Assert.IsTrue(registry.TryGetRoute(GrossDemandWorkspaceViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(GrossDemandWorkspaceViewModel), route.ViewModelType);

        var group = registry.MenuItems.Single(item => item.RouteKey == "demand-planning");
        Assert.AreEqual("需求计划", group.Title);
        Assert.IsTrue(group.Children.Any(item => item.RouteKey == GrossDemandWorkspaceViewModel.RouteKey));
        Assert.IsTrue(group.Children.Any(item => item.RouteKey == NetDemandManagerViewModel.RouteKey));
        Assert.IsTrue(registry.TryGetRoute(NetDemandManagerViewModel.RouteKey, out var netDemandRoute));
        Assert.AreEqual(typeof(NetDemandManagerViewModel), netDemandRoute.ViewModelType);
        Assert.IsFalse(registry.MenuItems.Any(item => item.RouteKey == "planning"));
        Assert.IsTrue(registry.TryGetRoute("planning", out _));
    }

    [TestMethod]
    public void RouteRegistry_ShouldKeepLegacyPackingListRoutesUnavailable()
    {
        var registry = new NavigationRouteRegistry();

        Assert.IsFalse(registry.TryGetRoute("purchasing", out _));
        // 出货是菜单分组，不是可打开页面；具体箱单路由由 Feature 注册。
        Assert.IsFalse(registry.TryGetRoute("shipping", out _));
        Assert.IsFalse(registry.MenuItems.Any(item => item.RouteKey == "purchasing"));
        Assert.IsTrue(registry.MenuItems.Any(item => item.RouteKey == "shipping"));
        Assert.IsFalse(registry.TryGetRoute("shipping-packing-lists", out _));
        Assert.IsFalse(registry.TryGetRoute("shipping-packing-list-detail", out _));
    }

    [TestMethod]
    public async Task OpenAsync_ShouldReuseExistingTabAndRecreateAfterClose()
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(
            new NavigationRoute(CategoryRouteKey, "产品分类管理", "Workspace", typeof(TestPageViewModel), "test", null));
        var navigation = CreateNavigation(provider, registry);

        await navigation.OpenAsync(CategoryRouteKey);
        var firstTab = navigation.SelectedTab;
        var firstViewModel = (TestPageViewModel)firstTab!.ContentViewModel;

        await navigation.OpenAsync(CategoryRouteKey);

        Assert.AreEqual(1, navigation.OpenTabs.Count);
        Assert.AreSame(firstTab, navigation.SelectedTab);
        Assert.AreEqual(1, provider.CreatedCount);
        Assert.AreEqual(1, firstViewModel.InitializeCount);

        await navigation.CloseAsync(firstTab);
        await navigation.OpenAsync(CategoryRouteKey);

        var secondViewModel = (TestPageViewModel)navigation.SelectedTab!.ContentViewModel;
        Assert.AreEqual(1, navigation.OpenTabs.Count);
        Assert.AreEqual(2, provider.CreatedCount);
        Assert.AreNotSame(firstViewModel, secondViewModel);
        Assert.AreEqual(1, secondViewModel.InitializeCount);
    }

    [TestMethod]
    public async Task OpenDocumentAsync_ShouldDeduplicateByDocumentKeyAndProtectDirtyClose()
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(
            new NavigationRoute("document", "文档", "Workspace", typeof(TestDocumentViewModel), "test", null));
        var navigation = CreateNavigation(provider, registry);
        var parameter = new object();

        await navigation.OpenDocumentAsync("document", "sales-order:1", parameter);
        var firstTab = navigation.SelectedTab!;
        var document = (TestDocumentViewModel)firstTab.ContentViewModel;
        await navigation.OpenDocumentAsync("document", "sales-order:1", new object());

        Assert.AreEqual(1, navigation.OpenTabs.Count);
        Assert.AreEqual(1, document.InitializeCount);
        Assert.AreSame(parameter, document.Parameter);

        document.SetState("SO-1001", true);
        Assert.AreEqual("SO-1001", firstTab.Title);
        Assert.IsTrue(firstTab.IsDirty);

        document.AllowClose = false;
        await navigation.CloseAsync(firstTab);
        Assert.AreEqual(1, navigation.OpenTabs.Count);

        document.AllowClose = true;
        await navigation.CloseAsync(firstTab);
        Assert.AreEqual(0, navigation.OpenTabs.Count);
    }

    [TestMethod]
    public async Task UpdateDocumentKey_ShouldActivateExistingPersistedDocument()
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(
            new NavigationRoute("document", "文档", "Workspace", typeof(TestDocumentViewModel), "test", null));
        var navigation = CreateNavigation(provider, registry);

        await navigation.OpenDocumentAsync("document", "sales-order:existing");
        var existing = navigation.SelectedTab!;
        await navigation.OpenDocumentAsync("document", "sales-order:new:1");
        var created = navigation.SelectedTab!;

        navigation.UpdateDocumentKey(created.ContentViewModel, "sales-order:existing");

        Assert.AreEqual(1, navigation.OpenTabs.Count);
        Assert.AreSame(existing, navigation.SelectedTab);
    }

    [TestMethod]
    public void NavigationTabItem_Dispose_ShouldDisposeContentViewModel()
    {
        var viewModel = new DisposablePageViewModel();
        var tab = new NavigationTabItem(
            new NavigationRoute("disposable", "可释放页面", "Workspace", typeof(DisposablePageViewModel), "test", null),
            viewModel);

        tab.Dispose();

        Assert.IsTrue(viewModel.IsDisposed);
    }

    private static NavigationService CreateNavigation(
        TestServiceProvider provider,
        INavigationRouteRegistry registry)
    {
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = "test-user"
        });
        return new NavigationService(
            provider,
            registry,
            new NavigationAccessService(registry, currentUser),
            currentUser,
            new TestStatusMessageService(),
            NullLogger<NavigationService>.Instance);
    }

    private sealed class TestServiceProvider : INavigationTabFactory
    {
        public int CreatedCount { get; private set; }

        public INavigationTabLease Create(NavigationRoute route)
        {
            object viewModel;
            if (route.ViewModelType == typeof(TestDocumentViewModel))
            {
                CreatedCount++;
                viewModel = new TestDocumentViewModel();
            }
            else if (route.ViewModelType == typeof(TestPageViewModel))
            {
                CreatedCount++;
                viewModel = new TestPageViewModel();
            }
            else
            {
                throw new InvalidOperationException($"未注册测试 ViewModel：{route.ViewModelType.Name}");
            }

            return new TestLease(viewModel);
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

    private sealed class TestRouteRegistry(NavigationRoute route) : INavigationRouteRegistry
    {
        public IReadOnlyList<NavigationRoute> Routes { get; } = [route];

        public IReadOnlyList<NavigationMenuItem> MenuItems { get; } = [];

        public bool TryGetRoute(string routeKey, out NavigationRoute foundRoute)
        {
            foundRoute = route;
            return string.Equals(routeKey, route.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class TestPageViewModel : IAsyncInitializable
    {
        public int InitializeCount { get; private set; }

        public Task InitializeAsync()
        {
            InitializeCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DisposablePageViewModel : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TestDocumentViewModel : IDocumentTabViewModel, IDocumentTabInitializable, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string DocumentTitle { get; private set; } = "新文档";
        public bool HasUnsavedChanges { get; private set; }
        public bool AllowClose { get; set; } = true;
        public int InitializeCount { get; private set; }
        public object? Parameter { get; private set; }

        public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
        {
            InitializeCount++;
            Parameter = parameter;
            return Task.CompletedTask;
        }

        public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(AllowClose);

        public void SetState(string title, bool isDirty)
        {
            DocumentTitle = title;
            HasUnsavedChanges = isDirty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DocumentTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnsavedChanges)));
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

        public void Show(string title, string message, UiMessageSeverity severity)
        {
        }

        public void Clear()
        {
        }
    }
}

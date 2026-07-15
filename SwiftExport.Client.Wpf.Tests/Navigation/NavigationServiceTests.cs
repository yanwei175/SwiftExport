using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;

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
    }

    [TestMethod]
    public async Task OpenAsync_ShouldReuseExistingTabAndRecreateAfterClose()
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(
            new NavigationRoute(CategoryRouteKey, "产品分类管理", "Workspace", typeof(TestPageViewModel)));
        var navigation = new NavigationService(provider, registry, new TestStatusMessageService());

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

    private sealed class TestServiceProvider : IServiceProvider
    {
        public int CreatedCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType != typeof(TestPageViewModel))
            {
                return null;
            }

            CreatedCount++;
            return new TestPageViewModel();
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

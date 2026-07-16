using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;
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

    [TestMethod]
    public async Task OpenDocumentAsync_ShouldDeduplicateByDocumentKeyAndProtectDirtyClose()
    {
        var provider = new TestServiceProvider();
        var registry = new TestRouteRegistry(
            new NavigationRoute("document", "文档", "Workspace", typeof(TestDocumentViewModel)));
        var navigation = new NavigationService(provider, registry, new TestStatusMessageService());
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
            new NavigationRoute("document", "文档", "Workspace", typeof(TestDocumentViewModel)));
        var navigation = new NavigationService(provider, registry, new TestStatusMessageService());

        await navigation.OpenDocumentAsync("document", "sales-order:existing");
        var existing = navigation.SelectedTab!;
        await navigation.OpenDocumentAsync("document", "sales-order:new:1");
        var created = navigation.SelectedTab!;

        navigation.UpdateDocumentKey(created.ContentViewModel, "sales-order:existing");

        Assert.AreEqual(1, navigation.OpenTabs.Count);
        Assert.AreSame(existing, navigation.SelectedTab);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public int CreatedCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(TestDocumentViewModel))
            {
                CreatedCount++;
                return new TestDocumentViewModel();
            }

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

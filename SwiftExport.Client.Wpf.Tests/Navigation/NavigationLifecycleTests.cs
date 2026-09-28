using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Navigation;

/// <summary>验证业务页签容量、重复激活与导航生命周期边界。</summary>
[TestClass]
public sealed class NavigationLifecycleTests
{
    /// <summary>首页不占名额，第二十一个业务页签应在创建资源前被拒绝。</summary>
    [TestMethod]
    public async Task Capacity_ShouldAllowTwentyBusinessTabsExcludeHomeAndRejectTwentyFirstWithoutCreatingResources()
    {
        var routes = new[] { Route("home", canClose: false) }
            .Concat(Enumerable.Range(1, 21).Select(index => Route($"business-{index}")))
            .ToArray();
        var factory = new TrackingFactory(_ => new PlainViewModel());
        var context = CreateContext(routes, factory);

        await context.Navigation.OpenAsync("home");
        foreach (var index in Enumerable.Range(1, 20))
        {
            await context.Navigation.OpenAsync($"business-{index}");
        }

        await context.Navigation.OpenAsync("business-21");

        Assert.AreEqual(21, context.Navigation.OpenTabs.Count);
        Assert.AreEqual(21, factory.CreatedCount);
        Assert.AreEqual("business-20", context.Navigation.SelectedTab?.RouteKey);
        StringAssert.Contains(context.Status.LastMessage, "最多同时打开 20 个业务页签");
    }

    /// <summary>达到容量上限后仍可激活已存在的页签。</summary>
    [TestMethod]
    public async Task FullCapacity_ShouldStillActivateExistingTab()
    {
        var routes = Enumerable.Range(1, 20).Select(index => Route($"business-{index}")).ToArray();
        var factory = new TrackingFactory(_ => new PlainViewModel());
        var context = CreateContext(routes, factory);
        foreach (var route in routes)
        {
            await context.Navigation.OpenAsync(route.Key);
        }

        var existing = context.Navigation.OpenTabs[0];
        await context.Navigation.OpenAsync(existing.RouteKey);

        Assert.AreSame(existing, context.Navigation.SelectedTab);
        Assert.AreEqual(20, factory.CreatedCount);
    }

    [TestMethod]
    public async Task ConcurrentOpen_ShouldRegisterPlaceholderBeforeAwaitAndRejectCloseUntilInitializationCompletes()
    {
        var viewModel = new ControlledInitializable();
        var factory = new TrackingFactory(_ => viewModel);
        var context = CreateContext([Route("slow")], factory);

        var firstOpen = context.Navigation.OpenAsync("slow");
        var placeholder = context.Navigation.SelectedTab!;
        var duplicateOpen = context.Navigation.OpenAsync("slow");
        await context.Navigation.CloseAsync(placeholder);

        Assert.AreEqual(1, context.Navigation.OpenTabs.Count);
        Assert.IsTrue(placeholder.IsInitializing);
        Assert.IsFalse(placeholder.CanClose);
        Assert.AreEqual(1, factory.CreatedCount);
        Assert.IsTrue(duplicateOpen.IsCompleted);

        viewModel.Complete();
        await firstOpen;

        Assert.IsFalse(placeholder.IsInitializing);
        Assert.IsTrue(placeholder.CanClose);
    }

    [TestMethod]
    public async Task FailedInitialization_ShouldRemovePlaceholderDisposeOnceAndRestorePreviousSelection()
    {
        var factory = new TrackingFactory(route => route.Key == "good"
            ? new PlainViewModel()
            : new FailingInitializable());
        var context = CreateContext([Route("good"), Route("bad")], factory);
        await context.Navigation.OpenAsync("good");
        var previous = context.Navigation.SelectedTab;

        await context.Navigation.OpenAsync("bad");

        Assert.AreEqual(1, context.Navigation.OpenTabs.Count);
        Assert.AreSame(previous, context.Navigation.SelectedTab);
        Assert.AreEqual(1, factory.Leases.Single(lease => lease.RouteKey == "bad").DisposeCount);
        Assert.AreEqual(UiMessageSeverity.Error, context.Status.LastSeverity);
    }

    [TestMethod]
    public async Task ResetDuringDocumentInitialization_ShouldCancelRemoveAndNeverReattachOrDoubleDispose()
    {
        var viewModel = new ControlledDocumentInitializable();
        var factory = new TrackingFactory(_ => viewModel);
        var context = CreateContext([Route("document")], factory);

        var open = context.Navigation.OpenDocumentAsync("document", "document:1");
        var lease = factory.Leases.Single();
        context.Navigation.Reset();

        Assert.IsTrue(await viewModel.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        viewModel.CompleteIgnoringCancellation();
        await open;

        Assert.AreEqual(0, context.Navigation.OpenTabs.Count);
        Assert.IsNull(context.Navigation.SelectedTab);
        Assert.AreEqual(1, lease.DisposeCount);
        Assert.AreEqual(string.Empty, context.Status.LastMessage);
    }

    [TestMethod]
    public async Task RealFactory_ShouldIsolateScopedDependenciesAndDisposeEachTabScopeOnce()
    {
        ScopedDependency.DisposeCount = 0;
        ScopedPageViewModel.DisposeCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddTransient<ScopedPageViewModel>();
        using var provider = services.BuildServiceProvider();
        var factory = new NavigationTabFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var routes = new[] { Route("one", typeof(ScopedPageViewModel)), Route("two", typeof(ScopedPageViewModel)) };
        var context = CreateContext(routes, factory);

        await context.Navigation.OpenAsync("one");
        var first = (ScopedPageViewModel)context.Navigation.SelectedTab!.ContentViewModel!;
        await context.Navigation.OpenAsync("two");
        var second = (ScopedPageViewModel)context.Navigation.SelectedTab!.ContentViewModel!;

        Assert.AreNotSame(first.Dependency, second.Dependency);
        await context.Navigation.CloseAsync(context.Navigation.OpenTabs[0]);
        context.Navigation.Reset();
        context.Navigation.Reset();

        Assert.AreEqual(2, ScopedDependency.DisposeCount);
        Assert.AreEqual(2, ScopedPageViewModel.DisposeCount);
    }

    [TestMethod]
    public async Task DocumentKeyMerge_ShouldDisposeMergedScopeOnceAndKeepExistingScopeUntilReset()
    {
        var factory = new TrackingFactory(_ => new MergeDocumentViewModel());
        var context = CreateContext([Route("document", typeof(MergeDocumentViewModel))], factory);
        await context.Navigation.OpenDocumentAsync("document", "document:existing");
        var existing = context.Navigation.SelectedTab!;
        await context.Navigation.OpenDocumentAsync("document", "document:new");
        var created = context.Navigation.SelectedTab!;

        context.Navigation.UpdateDocumentKey(created.ContentViewModel!, "document:existing");

        Assert.AreEqual(1, context.Navigation.OpenTabs.Count);
        Assert.AreSame(existing, context.Navigation.SelectedTab);
        Assert.AreEqual(0, factory.Leases[0].DisposeCount);
        Assert.AreEqual(1, factory.Leases[1].DisposeCount);

        context.Navigation.Reset();
        context.Navigation.Reset();
        Assert.AreEqual(1, factory.Leases[0].DisposeCount);
        Assert.AreEqual(1, factory.Leases[1].DisposeCount);
    }

    private static NavigationRoute Route(string key, bool canClose = true) =>
        Route(key, typeof(PlainViewModel), canClose);

    private static NavigationRoute Route(string key, Type viewModelType, bool canClose = true) =>
        new(key, key, "test", viewModelType, "test", null, canClose);

    private static TestContext CreateContext(
        IReadOnlyList<NavigationRoute> routes,
        INavigationTabFactory factory)
    {
        var registry = new TestRouteRegistry(routes);
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), UserName = "test" });
        var status = new TestStatusMessageService();
        var navigation = new NavigationService(
            factory,
            registry,
            new NavigationAccessService(registry, currentUser),
            currentUser,
            status,
            NullLogger<NavigationService>.Instance);
        return new TestContext(navigation, status);
    }

    private sealed record TestContext(NavigationService Navigation, TestStatusMessageService Status);

    private sealed class TestRouteRegistry(IReadOnlyList<NavigationRoute> routes) : INavigationRouteRegistry
    {
        public IReadOnlyList<NavigationRoute> Routes => routes;
        public IReadOnlyList<NavigationMenuItem> MenuItems => [];

        public bool TryGetRoute(string routeKey, out NavigationRoute route)
        {
            route = routes.FirstOrDefault(candidate => string.Equals(candidate.Key, routeKey, StringComparison.OrdinalIgnoreCase))!;
            return route is not null;
        }
    }

    private sealed class TrackingFactory(Func<NavigationRoute, object> createViewModel) : INavigationTabFactory
    {
        public int CreatedCount { get; private set; }
        public List<TrackingLease> Leases { get; } = [];

        public INavigationTabLease Create(NavigationRoute route)
        {
            CreatedCount++;
            var lease = new TrackingLease(route.Key, createViewModel(route));
            Leases.Add(lease);
            return lease;
        }
    }

    private sealed class TrackingLease(string routeKey, object viewModel) : INavigationTabLease
    {
        private object? _viewModel = viewModel;
        public string RouteKey { get; } = routeKey;
        public int DisposeCount { get; private set; }
        public object ViewModel => _viewModel ?? throw new ObjectDisposedException(nameof(TrackingLease));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _viewModel, null) is not null)
            {
                DisposeCount++;
            }
        }
    }

    private sealed class PlainViewModel;

    private sealed class ControlledInitializable : IAsyncInitializable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync() => _completion.Task;
        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FailingInitializable : IAsyncInitializable
    {
        public Task InitializeAsync() => throw new InvalidOperationException("test failure");
    }

    private sealed class ControlledDocumentInitializable : IDocumentTabInitializable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult(true));
            await _completion.Task;
        }

        public void CompleteIgnoringCancellation() => _completion.TrySetResult();
    }

    private sealed class MergeDocumentViewModel : IDocumentTabInitializable
    {
        public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ScopedDependency : IDisposable
    {
        public static int DisposeCount { get; set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ScopedPageViewModel(ScopedDependency dependency) : IDisposable
    {
        public static int DisposeCount { get; set; }
        public ScopedDependency Dependency { get; } = dependency;
        public void Dispose() => DisposeCount++;
    }

    private sealed class TestStatusMessageService : IStatusMessageService
    {
        public event EventHandler<StatusMessageChangedEventArgs>? MessageChanged { add { } remove { } }
        public string? Title => null;
        public string? Message => LastMessage;
        public UiMessageSeverity Severity => LastSeverity;
        public bool IsOpen => !string.IsNullOrEmpty(LastMessage);
        public string LastMessage { get; private set; } = string.Empty;
        public UiMessageSeverity LastSeverity { get; private set; }
        public void Show(string title, string message, UiMessageSeverity severity) { LastMessage = message; LastSeverity = severity; }
        public void Clear() => LastMessage = string.Empty;
    }
}

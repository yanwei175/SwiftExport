using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Services;

[TestClass]
public sealed class ReferenceDataCacheServiceTests
{
    [TestMethod]
    public async Task EnsureLoadedAsync_ShouldCoalesceConcurrentFirstLoad()
    {
        var colorGate = new TaskCompletionSource<ServiceResult<List<ProductColorDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var categoryGate = new TaskCompletionSource<ServiceResult<List<ProductCategoryDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var colorApi = new ProductColorApiStub(() => colorGate.Task);
        var categoryApi = new ProductCategoryApiStub(() => categoryGate.Task);
        using var cache = CreateCache(colorApi, categoryApi);

        var first = cache.EnsureLoadedAsync();
        var second = cache.EnsureLoadedAsync();

        colorGate.SetResult(ServiceResult<List<ProductColorDto>>.Ok([Color("RED")]));
        categoryGate.SetResult(ServiceResult<List<ProductCategoryDto>>.Ok([Category("ROOT", 10)]));
        var results = await Task.WhenAll(first, second);
        var third = await cache.EnsureLoadedAsync();

        Assert.IsTrue(results.All(result => result.Success));
        Assert.IsTrue(third.Success);
        Assert.AreEqual(1, colorApi.GetAllCallCount);
        Assert.AreEqual(1, categoryApi.GetAllCallCount);
    }

    [TestMethod]
    public async Task RefreshAllAsync_ShouldBuildSortedTreeAndPromoteInvalidRelationships()
    {
        var root = Category("ROOT", 20);
        var child = Category("CHILD", 1, root.Uid);
        var orphan = Category("ORPHAN", 5, Guid.NewGuid());
        var self = Category("SELF", 6);
        self.ParentId = self.Uid;
        var cycleA = Category("CYCLE-A", 7);
        var cycleB = Category("CYCLE-B", 8, cycleA.Uid);
        cycleA.ParentId = cycleB.Uid;
        var colorApi = new ProductColorApiStub(() => SuccessColors(Color("BLUE"), Color("RED")));
        var categoryApi = new ProductCategoryApiStub(
            () => SuccessCategories(root, child, orphan, self, cycleA, cycleB));
        using var cache = CreateCache(colorApi, categoryApi);

        var result = await cache.RefreshAllAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, cache.ProductColors.Count);
        var cachedRoot = cache.ProductCategoryTree.Single(node => node.Uid == root.Uid);
        Assert.AreEqual(child.Uid, cachedRoot.Children.Single().Uid);
        CollectionAssert.IsSubsetOf(
            new[] { orphan.Uid, self.Uid, cycleA.Uid, cycleB.Uid },
            cache.ProductCategoryTree.Select(node => node.Uid).ToArray());
        CollectionAssert.AreEqual(
            cache.ProductCategoryTree.OrderBy(node => node.SortNo).ThenBy(node => node.CategoryName).Select(node => node.Uid).ToArray(),
            cache.ProductCategoryTree.Select(node => node.Uid).ToArray());
    }

    [TestMethod]
    public async Task Refresh_ShouldKeepCollectionInstanceAndPublishSingleReset()
    {
        var colorResponses = new Queue<ServiceResult<List<ProductColorDto>>>(
        [
            ServiceResult<List<ProductColorDto>>.Ok([Color("OLD")]),
            ServiceResult<List<ProductColorDto>>.Ok([Color("NEW")])
        ]);
        var colorApi = new ProductColorApiStub(() => Task.FromResult(colorResponses.Dequeue()));
        using var cache = CreateCache(colorApi, new ProductCategoryApiStub(() => SuccessCategories()));
        var collection = cache.ProductColors;
        var resetCount = 0;
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        await cache.RefreshProductColorsAsync();
        await cache.RefreshProductColorsAsync();

        Assert.AreSame(collection, cache.ProductColors);
        Assert.AreEqual(2, resetCount);
        Assert.AreEqual("NEW", cache.ProductColors.Single().ColorCode);
    }

    [TestMethod]
    public async Task RefreshAllAsync_WhenOneRequestFails_ShouldRetainBothOldSnapshots()
    {
        var oldColor = Color("OLD");
        var oldCategory = Category("OLD", 1);
        var colorResponses = new Queue<ServiceResult<List<ProductColorDto>>>(
        [
            ServiceResult<List<ProductColorDto>>.Ok([oldColor]),
            ServiceResult<List<ProductColorDto>>.Ok([Color("NEW")])
        ]);
        var categoryResponses = new Queue<ServiceResult<List<ProductCategoryDto>>>(
        [
            ServiceResult<List<ProductCategoryDto>>.Ok([oldCategory]),
            ServiceResult<List<ProductCategoryDto>>.Fail("分类接口失败")
        ]);
        using var cache = CreateCache(
            new ProductColorApiStub(() => Task.FromResult(colorResponses.Dequeue())),
            new ProductCategoryApiStub(() => Task.FromResult(categoryResponses.Dequeue())));

        Assert.IsTrue((await cache.RefreshAllAsync()).Success);
        var failed = await cache.RefreshAllAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(oldColor.Uid, cache.ProductColors.Single().Uid);
        Assert.AreEqual(oldCategory.Uid, cache.ProductCategoryTree.Single().Uid);
    }

    [TestMethod]
    public async Task SessionEnded_ShouldClearAndRequireNextSessionReload()
    {
        var session = new SessionServiceStub();
        var colorApi = new ProductColorApiStub(() => SuccessColors(Color("RED")));
        var categoryApi = new ProductCategoryApiStub(() => SuccessCategories(Category("ROOT", 1)));
        using var cache = CreateCache(colorApi, categoryApi, session);
        await cache.EnsureLoadedAsync();

        session.EndSession(SessionEndReason.Logout);

        Assert.AreEqual(0, cache.ProductColors.Count);
        Assert.AreEqual(0, cache.ProductCategoryTree.Count);
        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);
        Assert.AreEqual(2, colorApi.GetAllCallCount);
        Assert.AreEqual(2, categoryApi.GetAllCallCount);
    }

    [TestMethod]
    public async Task SessionEnded_DuringLoad_ShouldIgnorePreviousSessionResponse()
    {
        var session = new SessionServiceStub();
        var colorGate = new TaskCompletionSource<ServiceResult<List<ProductColorDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var categoryGate = new TaskCompletionSource<ServiceResult<List<ProductCategoryDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = CreateCache(
            new ProductColorApiStub(() => colorGate.Task),
            new ProductCategoryApiStub(() => categoryGate.Task),
            session);
        var loading = cache.EnsureLoadedAsync();

        session.EndSession(SessionEndReason.Logout);
        colorGate.SetResult(ServiceResult<List<ProductColorDto>>.Ok([Color("OLD-SESSION")]));
        categoryGate.SetResult(ServiceResult<List<ProductCategoryDto>>.Ok([Category("OLD-SESSION", 1)]));
        var result = await loading;

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, cache.ProductColors.Count);
        Assert.AreEqual(0, cache.ProductCategoryTree.Count);
    }

    [TestMethod]
    public void ProductColorGetAll_ShouldMatchControllerRoute()
    {
        var method = typeof(IProductColorApi).GetMethod(nameof(IProductColorApi.GetAllAsync));
        var get = method?.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().SingleOrDefault();

        Assert.IsNotNull(method);
        Assert.IsNotNull(get);
        Assert.AreEqual("/all", get.Path);
    }

    [TestMethod]
    public void ClientServices_ShouldRegisterReferenceDataCacheAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSwiftExportClientServices(new ConfigurationBuilder().Build());

        var descriptor = services.Single(item => item.ServiceType == typeof(IReferenceDataCacheService));

        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.AreEqual(typeof(ReferenceDataCacheService), descriptor.ImplementationType);
    }

    private static ReferenceDataCacheService CreateCache(
        IProductColorApi colorApi,
        IProductCategoryApi categoryApi,
        ISessionService? session = null) =>
        new(colorApi, categoryApi, new DirectApiCallRunner(), session ?? new SessionServiceStub());

    private static ProductColorDto Color(string code) => new()
    {
        Uid = Guid.NewGuid(),
        ColorCode = code,
        ColorCodeUnique = code,
        ColorNameEn = code,
        ColorNameZh = code
    };

    private static ProductCategoryDto Category(string code, int sortNo, Guid? parentId = null) => new()
    {
        Uid = Guid.NewGuid(),
        CategoryCode = code,
        CategoryName = code,
        FakeName = code,
        SortNo = sortNo,
        ParentId = parentId
    };

    private static Task<ServiceResult<List<ProductColorDto>>> SuccessColors(params ProductColorDto[] colors) =>
        Task.FromResult(ServiceResult<List<ProductColorDto>>.Ok(colors.ToList()));

    private static Task<ServiceResult<List<ProductCategoryDto>>> SuccessCategories(params ProductCategoryDto[] categories) =>
        Task.FromResult(ServiceResult<List<ProductCategoryDto>>.Ok(categories.ToList()));

    private sealed class ProductColorApiStub(
        Func<Task<ServiceResult<List<ProductColorDto>>>> getAll) : IProductColorApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<List<ProductColorDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class ProductCategoryApiStub(
        Func<Task<ServiceResult<List<ProductCategoryDto>>>> getAll) : IProductCategoryApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<List<ProductCategoryDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class SessionServiceStub : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;

        public void ClearSession()
        {
        }

        public void EndSession(SessionEndReason reason) =>
            SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}

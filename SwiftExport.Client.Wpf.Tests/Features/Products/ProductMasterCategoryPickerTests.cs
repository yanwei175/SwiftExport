using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主型号顶部分类多选的直接勾选、后代展开以及失败后的筛选草稿恢复。</summary>
[TestClass]
public sealed class ProductMasterCategoryPickerTests
{
    /// <summary>父子直接勾选保持两项身份，请求按最新缓存展开多层后代且去重。</summary>
    [TestMethod]
    public async Task ParentAndChild_ShouldExpandLatestDescendantsWithoutChangingDirectSelection()
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        vm.FilterCategories.Add(fixture.Root);
        vm.FilterCategories.Add(fixture.Child);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        CollectionAssert.AreEquivalent(new[] { fixture.RootId, fixture.ChildId, fixture.GrandchildId }, fixture.Requests.Single().CategoryIds);
        CollectionAssert.AreEqual(new[] { fixture.RootId, fixture.ChildId }, vm.CategoryFilter.CategoryIds.ToArray());
        CollectionAssert.AreEqual(new[] { fixture.RootId, fixture.ChildId }, vm.FilterCategories.Select(row => row.Uid).ToArray());
        fixture.LoadTree(includeNewDescendant: true);
        await vm.SearchCommand.ExecuteAsync(null);
        CollectionAssert.AreEquivalent(new[] { fixture.RootId, fixture.ChildId, fixture.GrandchildId, fixture.NewDescendantId }, fixture.Requests.Last().CategoryIds);
        CollectionAssert.AreEqual(new[] { fixture.RootId, fixture.ChildId }, vm.CategoryFilter.CategoryIds.ToArray());
        Assert.AreEqual(2, vm.FilterCategories.Count, "后代展开只属于请求，不回写控件直接勾选。");
    }

    /// <summary>分类与未分类可以组合，仅未分类不限制为全部，全部入口为空勾选且未分类关闭。</summary>
    [TestMethod]
    public async Task UncategorizedAndEmptySelection_ShouldKeepDistinctQuerySemantics()
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        vm.FilterCategories.Add(fixture.Root);
        vm.FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.Requests[0].IncludeUncategorized);
        CollectionAssert.AreEquivalent(new[] { fixture.RootId, fixture.ChildId, fixture.GrandchildId }, fixture.Requests[0].CategoryIds);
        vm.FilterCategories.Clear();
        vm.FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.Requests[1].IncludeUncategorized);
        Assert.AreEqual(0, fixture.Requests[1].CategoryIds.Count);
        Assert.IsFalse(vm.CategoryFilter.IsAll);
        vm.FilterCategories.Clear();
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.IsFalse(fixture.Requests[2].IncludeUncategorized);
        Assert.AreEqual(0, fixture.Requests[2].CategoryIds.Count);
        Assert.IsTrue(vm.CategoryFilter.IsAll);
        Assert.AreEqual(0, vm.FilterCategories.Count);
    }

    /// <summary>取消放弃不查询并保留行草稿；查询失败恢复最后成功的分类及未分类勾选。</summary>
    [TestMethod]
    public async Task DiscardCancellationAndQueryFailure_ShouldRestoreSuccessfulDirectSelection()
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        vm.FilterCategories.Add(fixture.Root);
        vm.FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        vm.Masters.LoadFromDtos([new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "M" } }]);
        var row = vm.Masters.Items.Single();
        row.DefaultDescriptionZh = "未保存草稿";
        fixture.ConfirmDiscard = false;
        vm.FilterCategories.Clear(); vm.FilterCategories.Add(fixture.Child);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.Requests.Count, "取消放弃不能发出新查询。");
        Assert.AreEqual("未保存草稿", row.DefaultDescriptionZh);
        Assert.IsTrue(row.IsDirty);
        CollectionAssert.AreEqual(new[] { fixture.RootId, Guid.Empty }, vm.FilterCategories.Select(category => category.Uid).ToArray());
        Assert.IsTrue(vm.IncludeUncategorized);
        row.RejectChanges();
        fixture.FailQuery = true;
        vm.FilterCategories.Clear(); vm.FilterCategories.Add(fixture.Child);
        await vm.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.AreEqual(2, fixture.Requests.Count);
        CollectionAssert.AreEquivalent(new[] { fixture.ChildId, fixture.GrandchildId }, fixture.Requests.Last().CategoryIds);
        CollectionAssert.AreEqual(new[] { fixture.RootId }, vm.CategoryFilter.CategoryIds.ToArray());
        CollectionAssert.AreEqual(new[] { fixture.RootId, Guid.Empty }, vm.FilterCategories.Select(category => category.Uid).ToArray());
        Assert.IsTrue(vm.IncludeUncategorized);
        Assert.AreSame(row, vm.Masters.Items.Single(), "失败查询不替换现有列表。");
    }

    /// <summary>构造明确的离线三级树及受控查询回执，不修改共享缓存替身或请求真实 API。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly Guid RootId = Guid.NewGuid(), ChildId = Guid.NewGuid(), GrandchildId = Guid.NewGuid(), NewDescendantId = Guid.NewGuid();
        internal readonly ReferenceDataCacheServiceStub Cache = new();
        internal readonly List<ProductMasterSearchRequest> Requests = [];
        internal bool ConfirmDiscard = true, FailQuery;
        internal readonly ProductMasterManagerViewModel ViewModel;
        internal ProductCategoryCacheNode Root => Cache.ProductCategories.Single(row => row.Uid == RootId);
        internal ProductCategoryCacheNode Child => Cache.ProductCategories.Single(row => row.Uid == ChildId);

        internal Fixture()
        {
            LoadTree();
            var api = Proxy<IProductMasterApi>((method, args) =>
            {
                Assert.AreEqual(nameof(IProductMasterApi.GetPagedAsync), method.Name);
                Requests.Add((ProductMasterSearchRequest)args[0]!);
                return Task.FromResult(FailQuery
                    ? ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Fail("测试查询失败")
                    : ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new() { PageIndex = 1, PageSize = 20 }));
            });
            var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync)
                ? Task.FromResult(ConfirmDiscard) : throw new AssertFailedException(method.Name));
            ViewModel = new ProductMasterManagerViewModel(api,
                Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()), Cache,
                Proxy<IMessageService>((_, _) => null), dialogs, null!, null!, new ImageAddressResolverStub())
            { IsCardView = true };
        }

        /// <summary>共享替身只提供扁平候选，本夹具明确连接已知父子边，模拟运行缓存的完整树。</summary>
        internal void LoadTree(bool includeNewDescendant = false)
        {
            var rows = new List<ProductCategoryDto>
            {
                new() { Uid = RootId, CategoryName = "ROOT", FakeName = "父分类" },
                new() { Uid = ChildId, ParentId = RootId, CategoryName = "CHILD", FakeName = "子分类" },
                new() { Uid = GrandchildId, ParentId = ChildId, CategoryName = "GRANDCHILD", FakeName = "孙分类" }
            };
            if (includeNewDescendant) rows.Add(new() { Uid = NewDescendantId, ParentId = GrandchildId, CategoryName = "NEW", FakeName = "新后代" });
            Cache.SetProductCategories(rows);
            Root.AddChild(Child);
            var grandchild = Cache.ProductCategories.Single(row => row.Uid == GrandchildId);
            Child.AddChild(grandchild);
            if (includeNewDescendant) grandchild.AddChild(Cache.ProductCategories.Single(row => row.Uid == NewDescendantId));
        }

        /// <summary>释放页面订阅，保证离线夹具不在其他用例中接收缓存通知。</summary>
        public void Dispose() => ViewModel.Dispose();
    }

    /// <summary>复用既有代理类型，仅替换本用例需要的接口回执。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }
}

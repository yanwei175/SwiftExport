using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>主型号详情消费共享继承解析结果，分类切换和快照变化不得默默丢失选择。</summary>
public sealed partial class ProductMasterTagSelectionTests
{
    /// <summary>解析器提供的共享组不依赖具体分类字段，重复来源只呈现一个编辑器。</summary>
    [TestMethod]
    public void SharedResolution_ShouldShowOneGroupAndKeepSelectedIdentity()
    {
        var group = Definition();
        using var model = new ProductMasterTagSelectionModel();
        model.Load(Guid.NewGuid(), [group.Options[0].Uid], []);
        model.RefreshDefinitions(new(true, "", [group, group],
            [new() { Uid = Guid.NewGuid(), CategoryId = Guid.NewGuid(), AttributeGroupId = group.Uid },
             new() { Uid = Guid.NewGuid(), CategoryId = model.CategoryId, AttributeGroupId = group.Uid }]));
        Assert.AreEqual(1, model.Groups.Count);
        Assert.AreEqual("吸顶", model.Groups.Single().SelectionSummary);
        Assert.AreEqual(0, model.MissingSelections.Count);
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, model.GetSelectedOptionIds()!);
    }

    /// <summary>目标分类仍提供全部选择时无需确认，分类变化不把共同标签清空或产生假标签脏状态。</summary>
    [TestMethod]
    public async Task SharedCategoryChange_ShouldPreserveAllWithoutConfirmation()
    {
        var group = Definition(); var first = Category(FixtureCategoryId, "顶喷"); var next = Category(Guid.NewGuid(), "手持花洒");
        var cache = CacheWithResolution(_ => Resolution([group]), first, next);
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, null!,
            Proxy<IDialogService>((_, _) => throw new AssertFailedException("共同标签不需要清除确认。")), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedCategory = next; await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(next.Uid, vm.Header.CategoryId); Assert.AreEqual(next.Uid, vm.Tags.CategoryId);
        Assert.IsTrue(vm.Header.IsDirty); Assert.IsFalse(vm.Tags.IsDirty);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(first.Uid, vm.Header.CategoryId); Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(1, vm.Tags.Groups.Count);
    }

    /// <summary>确认只清除不再适用组中的值；取消保留完整旧分类和两组选择。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PartialCategoryChange_ShouldClearOnlyInapplicableValues(bool confirm)
    {
        var common = Definition(); var exclusive = Definition(); exclusive.GroupName = "尺寸";
        var first = Category(FixtureCategoryId, "顶喷"); var next = Category(Guid.NewGuid(), "手持花洒");
        var cache = CacheWithResolution(id => Resolution(id == first.Uid ? [common, exclusive] : [common]), first, next);
        string? prompt = null;
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, null!,
            Proxy<IDialogService>((_, args) => { prompt = (string)args[1]!; return Task.FromResult(confirm); }), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, common.Options[0].Uid, exclusive.Options[0].Uid);
        vm.Tags.RefreshDefinitions(Resolution([common, exclusive]));
        vm.SelectedCategory = next; await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.IsNotNull(prompt); StringAssert.Contains(prompt, "1 个");
        Assert.AreEqual(confirm ? next.Uid : first.Uid, vm.Header.CategoryId);
        Assert.AreEqual(confirm, vm.HasUnsavedChanges);
        CollectionAssert.AreEquivalent(confirm ? new[] { common.Options[0].Uid }
            : new[] { common.Options[0].Uid, exclusive.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.AreEqual(confirm ? 1 : 2, vm.Tags.Groups.Count);
    }

    /// <summary>目标祖先链失败不能被解释成全部不适用，分类和选择保持原样并提供错误。</summary>
    [TestMethod]
    [DataRow("分类层级存在循环。")]
    [DataRow("分类祖先缺失。")]
    [DataRow("标签定义尚未加载。")]
    public async Task FailedCategoryResolution_ShouldKeepCategoryAndSelections(string reason)
    {
        var group = Definition(); var first = Category(FixtureCategoryId, "顶喷"); var next = Category(Guid.NewGuid(), "新分类");
        var cache = CacheWithResolution(id => id == first.Uid ? Resolution([group]) : new(false, reason, [], []), first, next);
        string? warning = null;
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache,
            Proxy<IMessageService>((_, args) => { warning = (string)args[1]!; return null; }),
            Proxy<IDialogService>((_, _) => throw new AssertFailedException("解析失败不能请求清除全部选择。")), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedCategory = next; await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(first.Uid, vm.Header.CategoryId); Assert.AreSame(first, vm.SelectedCategory);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.HasUnsavedChanges); StringAssert.Contains(warning!, reason);
    }

    /// <summary>确认期间新快照扩大失效范围时拒绝接纳，避免清除用户未同意移除的共同选择。</summary>
    [TestMethod]
    public async Task ResolutionChangedDuringConfirmation_ShouldKeepOriginalDraft()
    {
        var common = Definition(); var exclusive = Definition(); var first = Category(FixtureCategoryId, "顶喷"); var next = Category(Guid.NewGuid(), "新分类");
        ProductAttributeResolution target = Resolution([common]);
        var confirmation = new TaskCompletionSource<bool>(); var warned = false;
        var cache = CacheWithResolution(id => id == first.Uid ? Resolution([common, exclusive]) : target, first, next);
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, Proxy<IMessageService>((_, _) => { warned = true; return null; }),
            Proxy<IDialogService>((_, _) => confirmation.Task), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, common.Options[0].Uid, exclusive.Options[0].Uid);
        vm.Tags.RefreshDefinitions(Resolution([common, exclusive]));
        vm.SelectedCategory = next; var pending = vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsCategoryChangePending); Assert.IsFalse(await vm.CanCloseAsync(default));
        target = Resolution([]); confirmation.SetResult(true); await pending;
        Assert.IsTrue(warned); Assert.AreEqual(first.Uid, vm.Header.CategoryId);
        CollectionAssert.AreEquivalent(new[] { common.Options[0].Uid, exclusive.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.IsFalse(vm.IsCategoryChangePending);
    }

    /// <summary>标签草稿已修改后祖先解析失败，保存必须阻断并保留身份，而非降级发送明确清空。</summary>
    [TestMethod]
    public void FailedResolutionWithDraft_ShouldKeepIdsAndBlockSavingSelection()
    {
        var group = Definition(); using var model = Workspace(group, group.Options[0].Uid);
        model.Groups.Single().SelectedItems.Add(group.Options[1]); model.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        model.RefreshDefinitions(new(false, "分类祖先缺失。", [], []));
        Assert.IsTrue(model.IsDirty); Assert.IsTrue(model.HasSelections); Assert.IsFalse(model.CanEdit);
        Assert.IsNull(model.GetSelectedOptionIds()); StringAssert.Contains(model.ValidateSelection(FixtureCategoryId)!, "分类祖先缺失");
        model.RefreshDefinitions(Resolution([group]));
        CollectionAssert.AreEquivalent(group.Options.Select(option => option.Uid).ToArray(), model.GetSelectedOptionIds()!);
        Assert.IsTrue(model.IsDirty);
    }

    /// <summary>保存确认等待时缓存失效，即使之前构造了空集合，也重验为 null 保持协议。</summary>
    [TestMethod]
    public async Task CacheClearDuringSaveConfirmation_ShouldSendNullInsteadOfStaleEmpty()
    {
        SaveProductMasterDetailRequest? sent = null;
        var confirmation = new TaskCompletionSource<ProductMasterSyncDialogResult>();
        var api = Proxy<IProductMasterApi>((_, args) =>
        {
            sent = (SaveProductMasterDetailRequest)args[0]!;
            return Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Fail("BusinessBlocked", "保留测试草稿"));
        });
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), null!, Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub { Respond = () => confirmation.Task });
        LoadTagDetail(vm, FixtureCategoryId); vm.Tags.RefreshDefinitions(Resolution([])); vm.Header.DefaultDescriptionZh = "其他字段草稿";
        var pending = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy);
        vm.Tags.RefreshDefinitions(new(false, "标签定义尚未加载。", [], []));
        confirmation.SetResult(new() { Confirmed = true }); await pending;
        Assert.IsNotNull(sent); Assert.IsNull(sent.SelectedOptionIds); Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>详情在分类确认期间释放后，迟到确认不得改写头部或丢弃选择。</summary>
    [TestMethod]
    public async Task DisposeDuringCategoryConfirmation_ShouldIgnoreLateAcceptance()
    {
        var group = Definition(); var first = Category(FixtureCategoryId, "顶喷"); var next = Category(Guid.NewGuid(), "新分类");
        var confirmation = new TaskCompletionSource<bool>();
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, Cache(group, first, next), null!,
            Proxy<IDialogService>((_, _) => confirmation.Task), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedCategory = next; var pending = vm.CommitCategoryCommand.ExecuteAsync(null);
        vm.Dispose(); confirmation.SetResult(true); await pending;
        Assert.AreEqual(first.Uid, vm.Header.CategoryId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
    }

    /// <summary>选择测试只模拟缓存解析契约，实际继承算法由缓存专项测试验证。</summary>
    private static IReferenceDataCacheService CacheWithResolution(Func<Guid, ProductAttributeResolution> resolve, params ProductCategoryCacheNode[] categories) =>
        Proxy<IReferenceDataCacheService>((method, args) => method.Name switch
        {
            nameof(IReferenceDataCacheService.ResolveProductAttributes) => resolve((Guid)args[0]!),
            "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>(categories)),
            _ => null
        });

    /// <summary>加载头部和标签基线，不在模型上人为建立具体分类归属字段。</summary>
    private static void LoadTagDetail(ProductMasterDetailViewModel vm, Guid category, params Guid[] selected)
    {
        vm.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "MASTER", CategoryId = category } });
        vm.Tags.Load(category, selected, []);
    }
}

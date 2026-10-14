using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>主型号详情消费共享模板解析结果，属性模板切换和快照变化不得默默丢失选择。</summary>
public sealed partial class ProductMasterTagSelectionTests
{
    /// <summary>修改或清空产品分类不解析新标签范围，也不丢弃模板及已选身份。</summary>
    [TestMethod]
    public async Task CategoryChange_ShouldKeepProfileAndAllTagSelections()
    {
        var group = Definition(); var profile = Profile(FixtureProfileId, "顶喷模板");
        using var vm = new ProductMasterDetailViewModel(null!, null!, CacheWithResolution(_ => Resolution([group]), profile), null!,
            Proxy<IDialogService>((_, _) => throw new AssertFailedException("分类变化不需要标签确认。")), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, profile.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        var categoryId = Guid.NewGuid();
        vm.SelectedCategory = new ProductCategoryCacheNode(new SwiftExport.Contracts.DTOs.Products.ProductCategoryDto { Uid = categoryId, CategoryName = "新分类" });
        await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(categoryId, vm.Header.CategoryId);
        Assert.AreEqual(profile.Uid, vm.Header.ProductAttributeProfileId);
        Assert.AreEqual(profile.Uid, vm.Tags.ProfileId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        await vm.ClearCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(Guid.Empty, vm.Header.CategoryId);
        Assert.AreEqual(profile.Uid, vm.Header.ProductAttributeProfileId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.Tags.IsDirty);
    }

    /// <summary>解析器提供的共享组不依赖具体属性模板归属字段，重复来源只呈现一个编辑器。</summary>
    [TestMethod]
    public void SharedResolution_ShouldShowOneGroupAndKeepSelectedIdentity()
    {
        var group = Definition();
        using var model = new ProductAttributeSelectionModel();
        model.Load(Guid.NewGuid(), [group.Options[0].Uid], []);
        model.RefreshDefinitions(new(true, "", [group, group],
            [new() { Uid = Guid.NewGuid(), ProductAttributeProfileId = Guid.NewGuid(), AttributeGroupId = group.Uid },
             new() { Uid = Guid.NewGuid(), ProductAttributeProfileId = model.ProfileId, AttributeGroupId = group.Uid }]));
        Assert.AreEqual(1, model.Groups.Count);
        Assert.AreEqual("吸顶", model.Groups.Single().SelectionSummary);
        Assert.AreEqual(0, model.MissingSelections.Count);
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, model.GetSelectedOptionIds()!);
    }

    /// <summary>目标属性模板仍提供全部选择时无需确认，属性模板变化不把共同标签清空或产生假标签脏状态。</summary>
    [TestMethod]
    public async Task SharedCategoryChange_ShouldPreserveAllWithoutConfirmation()
    {
        var group = Definition(); var first = Profile(FixtureProfileId, "顶喷"); var next = Profile(Guid.NewGuid(), "手持花洒");
        var cache = CacheWithResolution(_ => Resolution([group]), first, next);
        using var vm = new ProductMasterDetailViewModel(null!, null!, cache, null!,
            Proxy<IDialogService>((_, _) => throw new AssertFailedException("共同标签不需要清除确认。")), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedProfile = next; await vm.CommitProfileCommand.ExecuteAsync(null);
        Assert.AreEqual(next.Uid, vm.Header.ProductAttributeProfileId); Assert.AreEqual(next.Uid, vm.Tags.ProfileId);
        Assert.IsTrue(vm.Header.IsDirty); Assert.IsFalse(vm.Tags.IsDirty);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(first.Uid, vm.Header.ProductAttributeProfileId); Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(1, vm.Tags.Groups.Count);
    }

    /// <summary>确认只清除不再适用组中的值；取消保留完整旧属性模板和两组选择。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PartialCategoryChange_ShouldClearOnlyInapplicableValues(bool confirm)
    {
        var common = Definition(); var exclusive = Definition(); exclusive.GroupName = "尺寸";
        var first = Profile(FixtureProfileId, "顶喷"); var next = Profile(Guid.NewGuid(), "手持花洒");
        var cache = CacheWithResolution(id => Resolution(id == first.Uid ? [common, exclusive] : [common]), first, next);
        string? prompt = null;
        using var vm = new ProductMasterDetailViewModel(null!, null!, cache, null!,
            Proxy<IDialogService>((_, args) => { prompt = (string)args[1]!; return Task.FromResult(confirm); }), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, common.Options[0].Uid, exclusive.Options[0].Uid);
        vm.Tags.RefreshDefinitions(Resolution([common, exclusive]));
        vm.SelectedProfile = next; await vm.CommitProfileCommand.ExecuteAsync(null);
        Assert.IsNotNull(prompt); StringAssert.Contains(prompt, "1 个");
        Assert.AreEqual(confirm ? next.Uid : first.Uid, vm.Header.ProductAttributeProfileId);
        Assert.AreEqual(confirm, vm.HasUnsavedChanges);
        CollectionAssert.AreEquivalent(confirm ? new[] { common.Options[0].Uid }
            : new[] { common.Options[0].Uid, exclusive.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.AreEqual(confirm ? 1 : 2, vm.Tags.Groups.Count);
    }

    /// <summary>目标模板解析失败不能被解释成全部不适用，属性模板和选择保持原样并提供错误。</summary>
    [TestMethod]
    [DataRow("属性模板定义不可用。")]
    [DataRow("属性模板定义缺失。")]
    [DataRow("标签定义尚未加载。")]
    public async Task FailedCategoryResolution_ShouldKeepCategoryAndSelections(string reason)
    {
        var group = Definition(); var first = Profile(FixtureProfileId, "顶喷"); var next = Profile(Guid.NewGuid(), "新属性模板");
        var cache = CacheWithResolution(id => id == first.Uid ? Resolution([group]) : new(false, reason, [], []), first, next);
        string? warning = null;
        using var vm = new ProductMasterDetailViewModel(null!, null!, cache,
            Proxy<IMessageService>((_, args) => { warning = (string)args[1]!; return null; }),
            Proxy<IDialogService>((_, _) => throw new AssertFailedException("解析失败不能请求清除全部选择。")), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedProfile = next; await vm.CommitProfileCommand.ExecuteAsync(null);
        Assert.AreEqual(first.Uid, vm.Header.ProductAttributeProfileId); Assert.AreSame(first, vm.SelectedProfile);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.HasUnsavedChanges); StringAssert.Contains(warning!, reason);
    }

    /// <summary>确认期间新快照扩大失效范围时拒绝接纳，避免清除用户未同意移除的共同选择。</summary>
    [TestMethod]
    public async Task ResolutionChangedDuringConfirmation_ShouldKeepOriginalDraft()
    {
        var common = Definition(); var exclusive = Definition(); var first = Profile(FixtureProfileId, "顶喷"); var next = Profile(Guid.NewGuid(), "新属性模板");
        ProductAttributeResolution target = Resolution([common]);
        var confirmation = new TaskCompletionSource<bool>(); var warned = false;
        var cache = CacheWithResolution(id => id == first.Uid ? Resolution([common, exclusive]) : target, first, next);
        using var vm = new ProductMasterDetailViewModel(null!, null!, cache, Proxy<IMessageService>((_, _) => { warned = true; return null; }),
            Proxy<IDialogService>((_, _) => confirmation.Task), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, common.Options[0].Uid, exclusive.Options[0].Uid);
        vm.Tags.RefreshDefinitions(Resolution([common, exclusive]));
        vm.SelectedProfile = next; var pending = vm.CommitProfileCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsProfileChangePending); Assert.IsFalse(await vm.CanCloseAsync(default));
        target = Resolution([]); confirmation.SetResult(true); await pending;
        Assert.IsTrue(warned); Assert.AreEqual(first.Uid, vm.Header.ProductAttributeProfileId);
        CollectionAssert.AreEquivalent(new[] { common.Options[0].Uid, exclusive.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.IsFalse(vm.IsProfileChangePending);
    }

    /// <summary>标签草稿已修改后模板解析失败，保存必须阻断并保留身份，而非降级发送明确清空。</summary>
    [TestMethod]
    public void FailedResolutionWithDraft_ShouldKeepIdsAndBlockSavingSelection()
    {
        var group = Definition(); using var model = Workspace(group, group.Options[0].Uid);
        model.Groups.Single().SelectedItems.Add(group.Options[1]); model.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        model.RefreshDefinitions(new(false, "属性模板定义缺失。", [], []));
        Assert.IsTrue(model.IsDirty); Assert.IsTrue(model.HasSelections); Assert.IsFalse(model.CanEdit);
        Assert.IsNull(model.GetSelectedOptionIds()); StringAssert.Contains(model.ValidateSelection(FixtureProfileId)!, "属性模板定义缺失");
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
        using var vm = new ProductMasterDetailViewModel(api, Runner(), null!, Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub { Respond = () => confirmation.Task });
        LoadTagDetail(vm, FixtureProfileId); vm.Tags.RefreshDefinitions(Resolution([])); vm.Header.DefaultDescriptionZh = "其他字段草稿";
        var pending = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy);
        vm.Tags.RefreshDefinitions(new(false, "标签定义尚未加载。", [], []));
        confirmation.SetResult(new() { Confirmed = true }); await pending;
        Assert.IsNotNull(sent); Assert.IsNull(sent.SelectedOptionIds); Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>详情在属性模板确认期间释放后，迟到确认不得改写头部或丢弃选择。</summary>
    [TestMethod]
    public async Task DisposeDuringCategoryConfirmation_ShouldIgnoreLateAcceptance()
    {
        var group = Definition(); var first = Profile(FixtureProfileId, "顶喷"); var next = Profile(Guid.NewGuid(), "新属性模板");
        var confirmation = new TaskCompletionSource<bool>();
        using var vm = new ProductMasterDetailViewModel(null!, null!, Cache(group, first, next), null!,
            Proxy<IDialogService>((_, _) => confirmation.Task), null!, new ProductMasterSyncDialogStub());
        LoadTagDetail(vm, first.Uid, group.Options[0].Uid); vm.Tags.RefreshDefinitions(Resolution([group]));
        vm.SelectedProfile = next; var pending = vm.CommitProfileCommand.ExecuteAsync(null);
        vm.Dispose(); confirmation.SetResult(true); await pending;
        Assert.AreEqual(first.Uid, vm.Header.ProductAttributeProfileId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
    }

    /// <summary>选择测试只模拟缓存解析契约，实际模板解析算法由缓存专项测试验证。</summary>
    private static IReferenceDataCacheService CacheWithResolution(Func<Guid, ProductAttributeResolution> resolve, params SysDictDataDto[] categories) =>
        Proxy<IReferenceDataCacheService>((method, args) => method.Name switch
        {
            nameof(IReferenceDataCacheService.ResolveProductAttributes) => resolve((Guid)args[0]!),
            nameof(IReferenceDataCacheService.GetDictionaryValues) => categories,
            "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>()),
            _ => null
        });

    /// <summary>加载头部和标签基线，不在模型上人为建立具体属性模板归属字段。</summary>
    private static void LoadTagDetail(ProductMasterDetailViewModel vm, Guid category, params Guid[] selected)
    {
        vm.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "MASTER", ProductAttributeProfileId = category } });
        vm.Tags.Load(category, selected, []);
    }
}

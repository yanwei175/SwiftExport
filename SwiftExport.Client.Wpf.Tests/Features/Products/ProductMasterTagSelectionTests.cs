using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主型号标签身份、基线、缓存重匹配、模式冲突及详情聚合提交，不访问网络。</summary>
[TestClass]
public sealed class ProductMasterTagSelectionTests
{
    /// <summary>多选的临时集合只有确定后成为可保存选择；重排和候选改名不产生假脏状态。</summary>
    [TestMethod]
    public void Multiple_CommitAndIdentityRefresh_ShouldKeepDraftAndBaseline()
    {
        var definition = Definition();
        using var model = Workspace(definition, definition.Options[0].Uid);
        var group = model.Groups.Single();
        group.SelectedItems.Add(definition.Options[1]);
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEquivalent(new[] { definition.Options[0].Uid }, model.GetSelectedOptionIds()!);
        group.CommitMultipleSelectionCommand.Execute(null);
        Assert.IsTrue(model.IsDirty);
        var refreshed = Definition(definition.CategoryId, definition.Uid, definition.Options.Select(item => item.Uid).ToArray());
        refreshed.Options[1].OptionName = "挂墙新名称";
        model.RefreshDefinitions([refreshed], true);
        Assert.IsTrue(model.IsDirty);
        Assert.AreSame(refreshed.Options[1], model.Groups.Single().SelectedItems[1]);
        Assert.IsTrue(model.Groups.Single().SelectionSummary.Contains("挂墙新名称"));
        model.RejectChanges();
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, model.GetSelectedOptionIds()!);
    }

    /// <summary>未加载与已加载空定义分别发送 null 和完整身份，缺失选择不会在刷新时被删除。</summary>
    [TestMethod]
    public void MissingDefinitions_ShouldPreserveIdsUntilExplicitRemoval()
    {
        var definition = Definition();
        var id = definition.Options[0].Uid;
        using var model = Workspace(definition, id);
        model.RefreshDefinitions([], false);
        Assert.IsNull(model.GetSelectedOptionIds());
        Assert.IsFalse(model.IsDirty);
        model.RefreshDefinitions([], true);
        CollectionAssert.AreEqual(new[] { id }, model.GetSelectedOptionIds()!);
        Assert.IsFalse(model.IsDirty);
        Assert.IsNotNull(model.ValidateSelection(definition.CategoryId));
        Assert.IsTrue(model.MissingSelections.Single().DisplayText.Contains("吸顶"));
        model.RemoveMissingSelectionCommand.Execute(model.MissingSelections.Single());
        Assert.IsTrue(model.IsDirty);
        Assert.AreEqual(0, model.GetSelectedOptionIds()!.Count);
        Assert.IsNull(model.ValidateSelection(definition.CategoryId));
    }

    /// <summary>多选改为单选保留旧多值并阻止保存，明确清除才修改最终身份集合。</summary>
    [TestMethod]
    public void ChangedSingleMode_ShouldKeepExistingMultipleValues()
    {
        var definition = Definition();
        using var model = Workspace(definition, definition.Options.Select(item => item.Uid).ToArray());
        var single = Definition(definition.CategoryId, definition.Uid, definition.Options.Select(item => item.Uid).ToArray());
        single.SelectionMode = ProductAttributeSelectionMode.Single;
        model.RefreshDefinitions([single], true);
        Assert.IsFalse(model.IsDirty);
        Assert.AreEqual(2, model.GetSelectedOptionIds()!.Count);
        Assert.IsTrue(model.Groups.Single().HasSingleSelectionConflict);
        Assert.IsNotNull(model.ValidateSelection(definition.CategoryId));
        model.Groups.Single().ClearSingleSelectionCommand.Execute(null);
        Assert.AreEqual(0, model.GetSelectedOptionIds()!.Count);
        Assert.IsTrue(model.IsDirty);
        Assert.IsNull(model.ValidateSelection(definition.CategoryId));
    }

    /// <summary>弹出层打开时缓存更新延迟到关闭，避免替换临时勾选，已确认身份保留。</summary>
    [TestMethod]
    public void PopupRefresh_ShouldWaitUntilClosedAndPreserveCommittedIds()
    {
        var definition = Definition();
        using var model = Workspace(definition, definition.Options[0].Uid);
        var group = model.Groups.Single();
        model.BeginPopupEdit();
        var next = Definition(definition.CategoryId, definition.Uid, definition.Options.Select(item => item.Uid).ToArray());
        next.GroupName = "新组名";
        model.RefreshDefinitions([next], true);
        Assert.AreSame(group, model.Groups.Single());
        group.SelectedItems.Add(definition.Options[1]);
        group.CommitMultipleSelectionCommand.Execute(null);
        model.EndPopupEdit();
        Assert.AreEqual("新组名", model.Groups.Single().GroupName);
        Assert.AreEqual(2, model.GetSelectedOptionIds()!.Count);
        Assert.IsTrue(model.IsDirty);
    }

    /// <summary>会话清理发生在弹出层打开时立即通知可用性，候选延迟重建也不能继续编辑。</summary>
    [TestMethod]
    public void PopupCacheClear_ShouldImmediatelyDisableEditingAndKeepIdentity()
    {
        var definition = Definition();
        using var model = Workspace(definition, definition.Options[0].Uid);
        var group = model.Groups.Single();
        var notified = false;
        model.StateChanged += (_, _) => notified = true;
        model.BeginPopupEdit();
        model.RefreshDefinitions([], false);
        Assert.IsTrue(notified);
        Assert.IsFalse(model.AreDefinitionsLoaded);
        Assert.IsFalse(model.CanEdit);
        Assert.IsTrue(model.StatusText.Contains("尚未加载"));
        Assert.IsNull(model.GetSelectedOptionIds());
        Assert.AreSame(group, model.Groups.Single());
        Assert.IsTrue(model.HasSelections);
        Assert.IsFalse(model.IsDirty);
        // 即使旧控件迟到提交，会话清理后的不可用工作区也不能修改最终身份。
        group.SelectedItems.Add(definition.Options[1]);
        group.CommitMultipleSelectionCommand.Execute(null);
        Assert.IsFalse(model.IsDirty);
        model.EndPopupEdit();
        Assert.AreEqual(0, model.Groups.Count);
        model.RefreshDefinitions([definition], true);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, model.GetSelectedOptionIds()!);
        Assert.IsFalse(model.IsDirty);
    }

    /// <summary>跨分类本地清除可撤销，定义不可用时不能把 null 保持当成明确清空。</summary>
    [TestMethod]
    public void CategoryChange_ShouldClearOnlyAfterExplicitCallAndRollback()
    {
        var definition = Definition();
        using var model = Workspace(definition, definition.Options[0].Uid);
        var newCategory = Guid.NewGuid();
        model.ChangeCategory(newCategory);
        Assert.AreEqual(0, model.Groups.Count, "不能继承其他分类定义。");
        Assert.AreEqual(0, model.GetSelectedOptionIds()!.Count);
        Assert.IsTrue(model.IsDirty);
        model.RefreshDefinitions([], false);
        Assert.IsNotNull(model.ValidateSelection(newCategory));
        model.RejectChanges();
        model.RefreshDefinitions([definition], true);
        Assert.AreEqual(definition.CategoryId, model.CategoryId);
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, model.GetSelectedOptionIds()!);
    }

    /// <summary>仅修改标签即可启用整页保存；失败保留草稿，成功接纳回执版本和完整选择。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DetailTagOnlySave_ShouldSendFinalSetAndAcceptOnlySuccess(bool succeed)
    {
        var definition = Definition();
        var masterId = Guid.NewGuid();
        SaveProductMasterDetailRequest? sent = null;
        var api = Proxy<IProductMasterApi>((_, args) =>
        {
            sent = (SaveProductMasterDetailRequest)args[0]!;
            return Task.FromResult(succeed
                ? ServiceResult<SaveProductMasterDetailResponse>.Ok(new() { Detail = new()
                { Uid = masterId, Version = 2, Master = sent.Master, SelectedOptionIds = sent.SelectedOptionIds } })
                : ServiceResult<SaveProductMasterDetailResponse>.Fail("BusinessBlocked", "测试拒绝"));
        });
        var cache = Cache(definition);
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), cache, Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub());
        vm.Header.Load(new() { Uid = masterId, Version = 1, Master = new() { MasterSku = "MASTER", CategoryId = definition.CategoryId } });
        vm.Tags.Load(definition.CategoryId, [], []);
        vm.Tags.RefreshDefinitions([definition], true);
        Assert.IsFalse(vm.HasUnsavedChanges);
        var group = vm.Tags.Groups.Single();
        group.SelectedItems.Add(definition.Options[0]);
        group.CommitMultipleSelectionCommand.Execute(null);
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(1, sent.ExpectedVersion);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, sent.SelectedOptionIds);
        Assert.AreEqual(!succeed, vm.HasUnsavedChanges);
        Assert.AreEqual(succeed ? 2 : 1, vm.Header.Version);
        Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
    }

    /// <summary>更换分类取消恢复 Lookup 与标签；确认清空仅修改草稿，整页放弃同时恢复。</summary>
    [TestMethod]
    public async Task DetailCategoryConfirmation_ShouldRestoreOrClearTags()
    {
        var definition = Definition();
        var first = Category(definition.CategoryId, "原分类");
        var next = Category(Guid.NewGuid(), "新分类");
        var confirmed = false;
        var cache = Cache(definition, first, next);
        var dialogs = Proxy<IDialogService>((_, _) => Task.FromResult(confirmed));
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, null!, dialogs, null!, new ProductMasterSyncDialogStub());
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Master = new() { MasterSku = "MASTER", CategoryId = first.Uid }, CategoryName = first.CategoryName });
        vm.Tags.Load(first.Uid, [definition.Options[0].Uid], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.SelectedCategory = next;
        await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(first.Uid, vm.Header.CategoryId);
        Assert.AreSame(first, vm.SelectedCategory);
        Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
        Assert.IsFalse(vm.HasUnsavedChanges);
        confirmed = true;
        vm.SelectedCategory = next;
        await vm.CommitCategoryCommand.ExecuteAsync(null);
        Assert.AreEqual(next.Uid, vm.Header.CategoryId);
        Assert.AreEqual(0, vm.Tags.GetSelectedOptionIds()!.Count);
        Assert.IsTrue(vm.HasUnsavedChanges);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(first.Uid, vm.Header.CategoryId);
        Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>新建主型号、中性产品与标签作为一次请求提交；回执接纳真实主型号身份。</summary>
    [TestMethod]
    public async Task NewDetail_ShouldSaveHeaderNeutralAndTagsTogether()
    {
        var definition = Definition();
        var savedId = Guid.NewGuid();
        SaveProductMasterDetailRequest? sent = null;
        string? documentKey = null;
        var api = Proxy<IProductMasterApi>((_, args) =>
        {
            sent = (SaveProductMasterDetailRequest)args[0]!;
            return Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Ok(new()
            { Detail = new() { Uid = savedId, Version = 1, Master = sent.Master, SelectedOptionIds = sent.SelectedOptionIds } }));
        });
        var navigation = Proxy<INavigationService>((method, args) =>
        {
            if (method.Name == nameof(INavigationService.UpdateDocumentKey)) documentKey = (string)args[1]!;
            return null;
        });
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), Cache(definition), Proxy<IMessageService>((_, _) => null),
            null!, navigation, new ProductMasterSyncDialogStub());
        vm.Header.Load(new() { Master = new() { MasterSku = "NEW-MASTER", CategoryId = definition.CategoryId } });
        vm.Header.MarkAsAdded();
        var neutral = vm.Neutrals.AddNewNeutral(Guid.Empty);
        neutral.ProductCode = "NEW-NEUTRAL";
        neutral.ColorId = Guid.NewGuid();
        vm.Tags.Load(definition.CategoryId, [], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.Tags.Groups.Single().SelectedItems.Add(definition.Options[0]);
        vm.Tags.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(Guid.Empty, sent.ProductMasterId);
        Assert.AreEqual(0, sent.ExpectedVersion);
        Assert.AreEqual("NEW-NEUTRAL", sent.CreateNeutrals.Single().Data.ProductCode);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, sent.SelectedOptionIds);
        Assert.AreEqual(savedId, vm.Header.Uid);
        Assert.AreEqual($"product-master:{savedId:N}", documentKey);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>标签首载失败时修改其他字段仍发送 null 保持协议，已有选择不被冒充空集合。</summary>
    [TestMethod]
    public async Task UnavailableDefinitions_ShouldKeepNullInDetailRequest()
    {
        SaveProductMasterDetailRequest? sent = null;
        var api = Proxy<IProductMasterApi>((_, args) =>
        {
            sent = (SaveProductMasterDetailRequest)args[0]!;
            return Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Fail("BusinessBlocked", "测试拒绝"));
        });
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), null!, Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub());
        var category = Guid.NewGuid();
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "MASTER", CategoryId = category } });
        vm.Tags.Load(category, [Guid.NewGuid()], []);
        vm.Header.DefaultDescriptionZh = "其他字段草稿";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.IsNull(sent.SelectedOptionIds);
        Assert.IsTrue(vm.Tags.HasSelections);
        Assert.IsFalse(vm.Tags.IsDirty);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>真正执行生成刷新命令，不因自身 IsRunning 提前退出；请求等待时拒绝重复并保留全部草稿。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RefreshCommand_ShouldExecuteAndPreserveAllDrafts(bool succeed)
    {
        var definition = Definition();
        var current = definition;
        var next = Definition(definition.CategoryId, definition.Uid, definition.Options.Select(item => item.Uid).ToArray());
        next.Options[0].OptionName = "吸顶新名称";
        var completion = new TaskCompletionSource<CacheRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
        {
            "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<ProductAttributeGroupDto>(new ObservableCollection<ProductAttributeGroupDto>([current])),
            "get_AreProductAttributesLoaded" => true,
            nameof(IReferenceDataCacheService.RefreshProductAttributesAsync) => Refresh(),
            _ => null
        });
        Task<CacheRefreshResult> Refresh() { calls++; return completion.Task; }
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub());
        vm.Tags.Load(definition.CategoryId, [], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.Header.ProductTag = "旧附加标签草稿";
        vm.Neutrals.AddNewNeutral(Guid.Empty).ProductCode = "中性产品草稿";
        vm.Tags.Groups.Single().SelectedItems.Add(definition.Options[0]);
        vm.Tags.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        var pending = vm.RefreshProductAttributesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        Assert.IsTrue(vm.IsBusy);
        await vm.RefreshProductAttributesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        if (succeed) current = next;
        completion.SetResult((CacheRefreshResult)Activator.CreateInstance(typeof(CacheRefreshResult), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [succeed, succeed ? "完成" : "测试失败"], null)!);
        await pending;
        Assert.IsFalse(vm.IsBusy);
        Assert.AreEqual("旧附加标签草稿", vm.Header.ProductTag);
        Assert.AreEqual("中性产品草稿", vm.Neutrals.Items.Single().ProductCode);
        Assert.IsTrue(vm.Tags.IsDirty);
        Assert.AreEqual(succeed ? "吸顶新名称" : "吸顶", vm.Tags.Groups.Single().SelectedItems.Single().OptionName);
        Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
    }

    /// <summary>只有查看权限时保留标签显示，编辑区、分类更换与保存入口拒绝写入。</summary>
    [TestMethod]
    public async Task DetailReadOnlyUser_ShouldShowSelectionsAndBlockWrites()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterView] });
        var definition = Definition();
        var calls = 0;
        var api = Proxy<IProductMasterApi>((_, _) => { calls++; throw new AssertFailedException("只读用户不能发送保存。"); });
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), Cache(definition), Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub(), user);
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "MASTER", CategoryId = definition.CategoryId } });
        vm.Tags.Load(definition.CategoryId, [definition.Options[0].Uid], []);
        vm.Tags.RefreshDefinitions([definition], true);
        Assert.IsTrue(vm.IsReadOnly);
        Assert.IsFalse(vm.CanEditTags);
        Assert.AreEqual("吸顶", vm.Tags.Groups.Single().SelectionSummary);
        Assert.IsFalse(vm.AddNeutralCommand.CanExecute(null));
        Assert.IsFalse(vm.CommitCategoryCommand.CanExecute(null));
        Assert.IsFalse(vm.ClearCategoryCommand.CanExecute(null));
        vm.Header.DefaultDescriptionZh = "程序性草稿";
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, calls);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>权限收回立即冻结编辑与保存且保留标签草稿；恢复权限可继续，释放后不响应身份事件。</summary>
    [TestMethod]
    public void PermissionRevoked_ShouldKeepDraftAndDisableAllTagWriteEntrances()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterEdit] });
        var definition = Definition();
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, Cache(definition), null!, null!, null!, new ProductMasterSyncDialogStub(), user);
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Master = new() { MasterSku = "MASTER", CategoryId = definition.CategoryId } });
        vm.Tags.Load(definition.CategoryId, [], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.Tags.Groups.Single().SelectedItems.Add(definition.Options[0]);
        vm.Tags.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        Assert.IsTrue(vm.CanEditTags);
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterView] });
        Assert.IsTrue(vm.IsReadOnly);
        Assert.IsFalse(vm.CanEditTags);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        Assert.IsFalse(vm.CommitCategoryCommand.CanExecute(null));
        Assert.IsFalse(vm.ClearCategoryCommand.CanExecute(null));
        Assert.IsTrue(vm.HasUnsavedChanges);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterEdit] });
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        vm.Dispose();
        var notified = false;
        vm.PropertyChanged += (_, _) => notified = true;
        user.Clear();
        Assert.IsFalse(notified);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    /// <summary>分类确认等待时收回权限，确认结果不能继续改变分类或清除已选标签。</summary>
    [TestMethod]
    public async Task PermissionRevokedDuringCategoryConfirmation_ShouldKeepCategoryAndTags()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterEdit] });
        var definition = Definition();
        var first = Category(definition.CategoryId, "原分类");
        var next = Category(Guid.NewGuid(), "新分类");
        var confirmation = new TaskCompletionSource<bool>();
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, Cache(definition, first, next), null!,
            Proxy<IDialogService>((_, _) => confirmation.Task), null!, new ProductMasterSyncDialogStub(), user);
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Master = new() { MasterSku = "MASTER", CategoryId = first.Uid }, CategoryName = first.CategoryName });
        vm.Tags.Load(first.Uid, [definition.Options[0].Uid], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.SelectedCategory = next;
        var pending = vm.CommitCategoryCommand.ExecuteAsync(null);
        user.Clear();
        confirmation.SetResult(true);
        await pending;
        Assert.AreEqual(first.Uid, vm.Header.CategoryId);
        Assert.AreSame(first, vm.SelectedCategory);
        Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
        Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.CanEditTags);
    }

    /// <summary>保存确认等待时收回权限，保留完整标签草稿且不发送原本尚未提交的请求。</summary>
    [TestMethod]
    public async Task PermissionRevokedDuringSaveConfirmation_ShouldPreventRequest()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterEdit] });
        var definition = Definition();
        var confirmation = new TaskCompletionSource<ProductMasterSyncDialogResult>();
        var calls = 0;
        var api = Proxy<IProductMasterApi>((_, _) => { calls++; throw new AssertFailedException("权限已收回不能发送请求。"); });
        using var vm = new ProductMasterDetailViewModel(api, null!, Runner(), Cache(definition), Proxy<IMessageService>((_, _) => null),
            null!, null!, new ProductMasterSyncDialogStub { Respond = () => confirmation.Task }, user);
        vm.Header.Load(new() { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "MASTER", CategoryId = definition.CategoryId } });
        vm.Tags.Load(definition.CategoryId, [], []);
        vm.Tags.RefreshDefinitions([definition], true);
        vm.Tags.Groups.Single().SelectedItems.Add(definition.Options[0]);
        vm.Tags.Groups.Single().CommitMultipleSelectionCommand.Execute(null);
        var pending = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);
        user.Clear();
        confirmation.SetResult(new() { Confirmed = true });
        await pending;
        Assert.AreEqual(0, calls);
        Assert.IsTrue(vm.Tags.IsDirty);
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.CanEditTags);
    }

    internal static ProductAttributeGroupDto Definition(Guid? category = null, Guid? group = null, Guid[]? ids = null) => new()
    {
        Uid = group ?? Guid.NewGuid(), CategoryId = category ?? Guid.NewGuid(), GroupName = "安装方式", SelectionMode = ProductAttributeSelectionMode.Multiple,
        Options = new[] { "吸顶", "挂墙" }.Select((name, index) => new ProductAttributeOptionDto
        { Uid = ids?[index] ?? Guid.NewGuid(), OptionName = name, OptionCode = index == 0 ? "CEILING" : "WALL", SortOrder = index }).ToList()
    };

    private static ProductMasterTagSelectionModel Workspace(ProductAttributeGroupDto definition, params Guid[] ids)
    {
        var model = new ProductMasterTagSelectionModel();
        model.RefreshDefinitions([definition], true);
        model.Load(definition.CategoryId, ids, []);
        return model;
    }

    private static ProductCategoryCacheNode Category(Guid id, string name) => (ProductCategoryCacheNode)Activator.CreateInstance(typeof(ProductCategoryCacheNode),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [new SwiftExport.Contracts.DTOs.Products.ProductCategoryDto { Uid = id, CategoryName = name }], null)!;

    private static IReferenceDataCacheService Cache(ProductAttributeGroupDto definition, params ProductCategoryCacheNode[] categories) =>
        Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
        {
            "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<ProductAttributeGroupDto>(new ObservableCollection<ProductAttributeGroupDto>([definition])),
            "get_AreProductAttributesLoaded" => true,
            "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>(categories)),
            _ => null
        });

    private static IApiCallRunner Runner() => Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}

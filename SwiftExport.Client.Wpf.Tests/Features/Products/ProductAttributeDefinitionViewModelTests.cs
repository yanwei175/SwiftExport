using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Attributes.ViewModels;
using SwiftExportClient.Features.Products.Attributes.Views;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证独立共享标签页的整页草稿、关联身份、聚合版本、权限及原生编辑保护。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductAttributeDefinitionViewModelTests
{
    /// <summary>新组、值和关联同批传临时父身份，成功恢复真实身份；缓存失败不回滚服务器提交。</summary>
    [TestMethod]
    public void Save_NewGroupOptionAndRelations_AcceptsIdentityWhenCacheRefreshFails() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(); await fixture.LoadAsync(); fixture.Cache.RefreshSuccess = false;
        fixture.Attributes.AddGroupCommand.Execute(null); var group = fixture.Attributes.SelectedGroup!;
        group.GroupCode = "MOUNT"; group.GroupName = "安装方式";
        fixture.Attributes.AddOptionCommand.Execute(null);
        fixture.Attributes.SelectedOption!.OptionCode = "CEILING"; fixture.Attributes.SelectedOption.OptionName = "吸顶";
        fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.First());
        var temporary = group.Uid;
        Assert.IsTrue(await fixture.Attributes.TrySaveAsync());
        var request = fixture.Api.LastRequest!;
        Assert.AreEqual(temporary, request.GroupChanges.CreateItems.Single().Data.ClientUid);
        Assert.AreEqual(temporary, request.OptionChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(temporary, request.ProfileChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(fixture.FirstProfileId, request.ProfileChanges.CreateItems.Single().Data.ProductAttributeProfileId);
        Assert.AreNotEqual(temporary, fixture.Attributes.SelectedGroup!.Uid);
        Assert.AreEqual(fixture.Attributes.SelectedGroup.Uid, fixture.Attributes.Options.Items.Single().AttributeGroupId);
        Assert.AreEqual(1, fixture.Attributes.SelectedGroup.Version);
        Assert.AreEqual(fixture.FirstProfileId, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Single().Uid);
        Assert.IsFalse(fixture.Attributes.HasUnsavedChanges);
        Assert.AreEqual(1, fixture.Cache.RefreshAttributeCalls);
        Assert.IsTrue(fixture.Messages.Warnings.Any(message => message.Contains("缓存")));
    });

    /// <summary>组删除的值及关联交给服务器级联，失败仍保留删除池和草稿，撤销恢复全部范围。</summary>
    [TestMethod]
    public void DeleteGroup_FailurePreservesPoolsAndRejectRestoresRelations() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        await fixture.Attributes.DeleteGroupCommand.ExecuteAsync(null);
        var request = fixture.Attributes.BuildRequest();
        Assert.AreEqual(1, request.GroupChanges.DeleteItems.Count);
        Assert.AreEqual(0, request.OptionChanges.DeleteItems.Count); Assert.AreEqual(0, request.ProfileChanges.DeleteItems.Count);
        Assert.AreEqual(1, fixture.Attributes.Options.DeletedItems.Count);
        fixture.Api.FailSave = true;
        Assert.IsFalse(await fixture.Attributes.TrySaveAsync()); Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
        Assert.AreEqual(1, fixture.Attributes.Groups.DeletedItems.Count);
        fixture.Attributes.RejectCommand.Execute(null);
        Assert.AreEqual(1, fixture.Attributes.Groups.Items.Count);
        Assert.AreEqual(1, fixture.Attributes.CurrentOptions.Cast<object>().Count());
        Assert.AreEqual(fixture.FirstProfileId, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Single().Uid);
        Assert.IsFalse(fixture.Attributes.HasUnsavedChanges);
    });

    /// <summary>切组保留所有组选项和关系草稿，同一次保存为每个既有受影响组附版本。</summary>
    [TestMethod]
    public void SwitchingGroup_PreservesOptionAndCategoryDrafts() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var first = fixture.Attributes.SelectedGroup!;
        fixture.Attributes.SelectedOption!.OptionName = "挂墙";
        fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        fixture.Attributes.AddGroupCommand.Execute(null);
        var second = fixture.Attributes.SelectedGroup!; second.GroupCode = "SIZE"; second.GroupName = "尺寸";
        fixture.Attributes.AddOptionCommand.Execute(null);
        fixture.Attributes.SelectedOption!.OptionCode = "8"; fixture.Attributes.SelectedOption.OptionName = "8寸";
        fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        fixture.Attributes.SelectedGroup = first;
        Assert.AreEqual("挂墙", fixture.Attributes.SelectedOption!.OptionName);
        Assert.AreEqual(2, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Count);
        var request = fixture.Attributes.BuildRequest();
        Assert.AreEqual(1, request.OptionChanges.UpdateItems.Count); Assert.AreEqual(1, request.OptionChanges.CreateItems.Count);
        Assert.AreEqual(2, request.ProfileChanges.CreateItems.Count);
        Assert.AreEqual(first.Uid, request.GroupChanges.UpdateItems.Single().Uid);
        Assert.AreEqual(first.Version, request.GroupChanges.UpdateItems.Single().Version);
        fixture.Attributes.SelectedGroup = second;
        Assert.AreEqual(1, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Count);
        Assert.AreEqual("8寸", fixture.Attributes.SelectedOption!.OptionName);
    });

    /// <summary>编码使用全局语义，首尾空白和大小写不能绕过本地重复校验。</summary>
    [TestMethod]
    public void DuplicateCodes_BlockPostingAndPreserveDraft() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        fixture.Attributes.AddGroupCommand.Execute(null);
        fixture.Attributes.SelectedGroup!.GroupCode = " mount "; fixture.Attributes.SelectedGroup.GroupName = "重复组";
        Assert.IsFalse(await fixture.Attributes.TrySaveAsync()); Assert.AreEqual(0, fixture.Api.SaveCalls);
        Assert.IsTrue(fixture.Attributes.HasUnsavedChanges); StringAssert.Contains(fixture.Attributes.ValidationSummary!, "重复");
    });

    /// <summary>无编辑权限仍读取完整定义，不能新增、修改或提交。</summary>
    [TestMethod]
    public void ReadWithoutEditPermission_LoadsButDisablesEditing() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true, canEdit: false); await fixture.LoadAsync();
        Assert.IsTrue(fixture.Attributes.IsLoaded); Assert.AreEqual(1, fixture.Attributes.Groups.Items.Count);
        Assert.IsFalse(fixture.Attributes.CanEditDefinitions); Assert.IsFalse(fixture.Attributes.AddGroupCommand.CanExecute(null));
        Assert.IsTrue(fixture.Attributes.AreGridsReadOnly); Assert.IsFalse(fixture.Attributes.CanEditCurrentGroup);
    });

    /// <summary>已有草稿在权限收回后保留，真实保存入口不再发送请求。</summary>
    [TestMethod]
    public void PermissionRevocation_PreservesDraftAndBlocksSave() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        fixture.Attributes.SelectedGroup!.GroupName = "草稿"; fixture.User.SetCurrentUser(new MeDto { UserId = "u", UserName = "test", Permissions = [] });
        Assert.IsFalse(fixture.Attributes.CanEditDefinitions); Assert.IsFalse(await fixture.Attributes.TrySaveAsync());
        Assert.AreEqual(0, fixture.Api.SaveCalls); Assert.AreEqual("草稿", fixture.Attributes.SelectedGroup.GroupName);
        Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
    });

    /// <summary>仅增删直接关系也提交既有组版本，软删除回执版本保持不变仍可成功接纳。</summary>
    [TestMethod]
    public void RelationOnlyChanges_TouchAggregateAndAcceptSoftDeleteReceipt() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true, groupVersion: 7); await fixture.LoadAsync();
        var group = fixture.Attributes.SelectedGroup!;
        fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Clear();
        fixture.Attributes.SelectedProfileSelection.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        var request = fixture.Attributes.BuildRequest();
        Assert.AreEqual(1, request.GroupChanges.UpdateItems.Count); Assert.AreEqual(7, request.GroupChanges.UpdateItems.Single().Version);
        Assert.AreEqual(0, request.OptionChanges.TotalCount);
        Assert.AreEqual(1, request.ProfileChanges.CreateItems.Count); Assert.AreEqual(3, request.ProfileChanges.DeleteItems.Single().Version);
        Assert.AreEqual(0, fixture.Attributes.Groups.ModifiedItems.Count);
        Assert.IsTrue(await fixture.Attributes.TrySaveAsync());
        Assert.AreEqual(8, fixture.Attributes.SelectedGroup!.Version);
        Assert.AreEqual(fixture.OtherProfileId, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Single().Uid);
        Assert.IsFalse(fixture.Attributes.HasUnsavedChanges);
    });

    /// <summary>未关联属性模板的组可以保存，属性模板关系不是新增组的必填项。</summary>
    [TestMethod]
    public void UnassignedGroup_CanSaveWithoutProfileChanges() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(); await fixture.LoadAsync();
        fixture.Attributes.AddGroupCommand.Execute(null);
        fixture.Attributes.SelectedGroup!.GroupCode = "EMPTY"; fixture.Attributes.SelectedGroup.GroupName = "未关联";
        Assert.IsTrue(await fixture.Attributes.TrySaveAsync());
        Assert.AreEqual(0, fixture.Api.LastRequest!.ProfileChanges.TotalCount); Assert.AreEqual(1, fixture.Attributes.Groups.Items.Count);
        Assert.AreEqual(0, fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Count);
    });

    /// <summary>取消刷新或关闭保持组选中和所有草稿，刷新没有发出替换读取。</summary>
    [TestMethod]
    public void CancelRefreshAndClose_PreservesAllDrafts() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var group = fixture.Attributes.SelectedGroup!;
        group.GroupName = "待保存"; fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        fixture.Dialogs.NextDecision = DialogResult.Close;
        await fixture.Attributes.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(await fixture.Attributes.CanCloseAsync(default));
        Assert.AreSame(group, fixture.Attributes.SelectedGroup); Assert.AreEqual("待保存", group.GroupName);
        Assert.AreEqual(2, fixture.Attributes.SelectedProfileSelection.SelectedProfiles.Count); Assert.AreEqual(1, fixture.Api.ReadCalls);
    });

    /// <summary>写入期间整页冻结且禁止关闭和重复保存；明确回执后恢复交互。</summary>
    [TestMethod]
    public void Write_FreezesPageUntilServerResponse() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        fixture.Attributes.SelectedGroup!.GroupName = "修改";
        var pause = fixture.Api.PauseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = fixture.Attributes.TrySaveAsync();
        Assert.IsTrue(fixture.Attributes.IsBusy); Assert.IsFalse(fixture.Attributes.IsInteractionEnabled);
        Assert.IsFalse(fixture.Attributes.AddGroupCommand.CanExecute(null)); Assert.IsFalse(fixture.Attributes.CanEditCurrentGroup);
        Assert.IsFalse(await fixture.Attributes.CanCloseAsync(default)); Assert.IsFalse(await fixture.Attributes.TrySaveAsync());
        Assert.AreEqual(1, fixture.Api.SaveCalls);
        pause.SetResult(true); Assert.IsTrue(await saving); Assert.IsTrue(fixture.Attributes.IsInteractionEnabled);
    });

    /// <summary>成功响应缺少提交快照时不接纳，保留全部草稿并要求核对服务器结果。</summary>
    [TestMethod]
    public void IncompleteSuccessSnapshot_PreservesDraftAndRequiresReload() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        fixture.Attributes.SelectedGroup!.GroupName = "尚未核对"; fixture.Api.IncompleteSnapshot = true;
        Assert.IsFalse(await fixture.Attributes.TrySaveAsync()); Assert.IsTrue(fixture.Attributes.RequiresReload);
        Assert.IsTrue(fixture.Attributes.HasUnsavedChanges); Assert.AreEqual("尚未核对", fixture.Attributes.SelectedGroup.GroupName);
        Assert.AreEqual(1, fixture.Attributes.SelectedGroup.Version); Assert.IsFalse(fixture.Attributes.CanEditDefinitions);
    });

    /// <summary>已经释放的页面不接纳忽略取消的晚到全局响应。</summary>
    [TestMethod]
    public void Dispose_IgnoresLateDefinitionResult() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        var pause = fixture.Api.PauseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = fixture.Attributes.InitializeAsync(); fixture.Attributes.Dispose(); pause.SetResult(true);
        await loading;
        Assert.IsFalse(fixture.Attributes.IsLoaded); Assert.AreEqual(0, fixture.Attributes.Groups.Items.Count);
    });

    /// <summary>重复进入合并同一次读取，登出取消旧请求且重新登录不能接纳旧页面响应。</summary>
    [TestMethod]
    public void RepeatedInitializationAndLogout_RejectOldSessionResult() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        var pause = fixture.Api.PauseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = fixture.Attributes.InitializeAsync(); await fixture.Attributes.InitializeAsync();
        Assert.AreEqual(1, fixture.Api.ReadCalls);
        fixture.User.Clear(); fixture.User.SetCurrentUser(new MeDto { UserId = "u", UserName = "test", Permissions = [PermissionKeys.ProductsCategoryEdit] });
        pause.SetResult(true); await loading;
        Assert.IsFalse(fixture.Attributes.IsLoaded); Assert.IsFalse(fixture.Attributes.IsInteractionEnabled);
        Assert.AreEqual(0, fixture.Attributes.Groups.Items.Count);
    });

    /// <summary>真实两张 Grid 的数值错误阻止保存和关闭，修正后提交模型仍受整页草稿保护。</summary>
    [TestMethod]
    public void View_CommitsNativeGridEditAndProtectsClose() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var view = new ProductAttributeManagerView { DataContext = fixture.Attributes };
        var window = new Window { Content = view, Width = 1400, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            var grid = (DataGrid)view.FindName("GroupGrid"); var optionGrid = (DataGrid)view.FindName("OptionGrid");
            Assert.AreEqual(5, grid.Columns.Count); Assert.AreEqual(4, optionGrid.Columns.Count);
            Assert.AreSame(fixture.Attributes.SelectedGroup, grid.SelectedItem);
            var group = fixture.Attributes.SelectedGroup!; grid.CurrentCell = new(group, grid.Columns[4]); Assert.IsTrue(grid.BeginEdit());
            await Dispatcher.Yield(DispatcherPriority.Background);
            var editor = (TextBox)grid.Columns[4].GetCellContent(group);
            editor.Text = "非法数字"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor)); Assert.IsFalse(view.CommitPendingEdits());
            Assert.IsFalse(await fixture.Attributes.CanCloseAsync(default), "尚未进入行模型的转换错误也必须阻止关闭。");
            editor.Text = "8"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(view.CommitPendingEdits()); Assert.AreEqual(8, group.SortOrder); Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
            fixture.Dialogs.NextDecision = DialogResult.Close; Assert.IsFalse(await fixture.Attributes.CanCloseAsync(default));
            Assert.AreEqual(8, fixture.Attributes.SelectedGroup!.SortOrder);
        }
        finally { window.Close(); }
    });

    /// <summary>管理属性模板入口先提交原生编辑，再打开路由并保留标签工作区全部草稿。</summary>
    [TestMethod]
    public void ManageProfiles_OpensRouteAndPreservesAllDefinitionDrafts() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var group = fixture.Attributes.SelectedGroup!; group.GroupName = "未保存组名";
        fixture.Attributes.Options.Items.Single().OptionName = "未保存值";
        fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        fixture.Attributes.CommitPendingEdits = () => false;
        await fixture.Attributes.ManageAttributeProfilesCommand.ExecuteAsync(null);
        Assert.IsNull(fixture.Navigation.LastRoute);
        fixture.Attributes.CommitPendingEdits = () => true;
        await fixture.Attributes.ManageAttributeProfilesCommand.ExecuteAsync(null);
        Assert.AreEqual(DictionaryDataManagerViewModel.RouteKey, fixture.Navigation.LastRoute);
        var request = (DictionaryDataDocumentOpenRequest)fixture.Navigation.LastParameter!;
        Assert.AreEqual(DictCodes.ProductAttributeProfile, request.DictCode);
        Assert.AreEqual(request.DocumentKey, fixture.Navigation.LastDocumentKey);
        Assert.AreSame(group, fixture.Attributes.SelectedGroup);
        Assert.AreEqual("未保存组名", group.GroupName);
        Assert.AreEqual("未保存值", fixture.Attributes.Options.Items.Single().OptionName);
        Assert.IsTrue(fixture.Attributes.SelectedProfileSelection.HasChanges);
        Assert.AreEqual(0, fixture.Api.SaveCalls);
    });

    /// <summary>模板缓存改名或缺失只重配候选，不能覆盖标签定义和关系身份草稿。</summary>
    [TestMethod]
    public void ProfileCacheChange_PreservesDraftIdsAndOriginalRelationBaseline() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var group = fixture.Attributes.SelectedGroup!; group.GroupName = "保留草稿";
        var selection = fixture.Attributes.SelectedProfileSelection!;
        selection.SelectedProfiles.Add(fixture.Attributes.ProfileCandidates.Last());
        fixture.Cache.Profiles = [new() { Uid = fixture.FirstProfileId, DictCode = DictCodes.ProductAttributeProfile, DataNameZh = "新模板名", IsEnabled = true }];
        fixture.Cache.RaiseChanged();
        Assert.AreSame(group, fixture.Attributes.SelectedGroup);
        Assert.AreEqual("保留草稿", group.GroupName);
        Assert.IsTrue(selection.HasChanges);
        Assert.AreEqual(2, selection.SelectedProfiles.Count);
        Assert.AreEqual("新模板名", selection.SelectedProfiles.Single(item => item.Uid == fixture.FirstProfileId).ProfileName);
        Assert.IsFalse(selection.SelectedProfiles.Single(item => item.Uid == fixture.OtherProfileId).IsAvailable);
        selection.SelectedProfiles.Remove(selection.SelectedProfiles.Single(item => item.Uid == fixture.OtherProfileId));
        Assert.IsFalse(selection.HasChanges, "重新匹配显示对象不得改变原关系身份基线。");
    });

    /// <summary>正在打开的多选弹出层延迟候选更新，关闭后保留已确认身份并显示新名称。</summary>
    [TestMethod]
    public void ProfileCacheChange_DefersWhilePopupIsOpen() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true); await fixture.LoadAsync();
        var open = true; fixture.Attributes.IsProfilePopupOpen = () => open;
        fixture.Cache.Profiles = [new() { Uid = fixture.FirstProfileId, DataNameZh = "关闭后显示", DictCode = DictCodes.ProductAttributeProfile, IsEnabled = true }];
        fixture.Cache.RaiseChanged();
        Assert.AreEqual("顶喷", fixture.Attributes.SelectedProfileSelection!.SelectedProfiles.Single().ProfileName);
        open = false; fixture.Attributes.ApplyPendingProfileRefresh();
        Assert.AreEqual("关闭后显示", fixture.Attributes.SelectedProfileSelection.SelectedProfiles.Single().ProfileName);
        Assert.IsFalse(fixture.Attributes.SelectedProfileSelection.HasChanges);
    });

    /// <summary>离线私有页面夹具，不依赖属性模板管理宿主或实际网络。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>创建一个登录会话、完整属性模板候选及标签 API，数据只归当前测试。</summary>
        public Fixture(bool withDefinition = false, bool canEdit = true, int groupVersion = 1)
        {
            User.SetCurrentUser(new MeDto { UserId = "u", UserName = "test", Permissions = canEdit ? [PermissionKeys.ProductsCategoryEdit, PermissionKeys.SystemDictionaryView] : [] });
            var service = DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>(); Cache = (CacheProxy)(object)service;
            Cache.Profiles = [new SysDictDataDto { Uid = FirstProfileId, DictCode = DictCodes.ProductAttributeProfile, DataNameZh = "顶喷", IsEnabled = true },
                new SysDictDataDto { Uid = OtherProfileId, DictCode = DictCodes.ProductAttributeProfile, DataNameZh = "手持花洒", IsEnabled = true }];
            Api = new AttributeApi(FirstProfileId, withDefinition, groupVersion);
            var navigation = DispatchProxy.Create<INavigationService, NavigationProxy>(); Navigation = (NavigationProxy)(object)navigation;
            Attributes = new(Api, new Runner(), service, Messages, Dialogs, User, navigation);
        }
        public Guid FirstProfileId { get; } = Guid.NewGuid();
        public Guid OtherProfileId { get; } = Guid.NewGuid();
        public CurrentUserService User { get; } = new();
        public AttributeApi Api { get; }
        public CacheProxy Cache { get; }
        public NavigationProxy Navigation { get; }
        public Messages Messages { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ProductAttributeManagerViewModel Attributes { get; }
        /// <summary>通过真实工作区初始化入口加载定义。</summary>
        public Task LoadAsync() => Attributes.InitializeAsync();
        /// <summary>释放本测试页面生命周期。</summary>
        public void Dispose() => Attributes.Dispose();
    }

    /// <summary>模拟全局原子定义协议，返回真实身份、聚合版本和完整快照。</summary>
    private sealed class AttributeApi : IProductAttributeApi
    {
        private readonly ProductAttributeDefinitionsDto _data = new();
        /// <summary>可选提供既有组、值和直接关系，便于验证聚合版本。</summary>
        public AttributeApi(Guid categoryId, bool withDefinition, int groupVersion)
        {
            if (!withDefinition) return;
            var group = new ProductAttributeGroupDto { Uid = Guid.NewGuid(), Version = groupVersion, GroupCode = "MOUNT", GroupName = "安装方式" };
            group.Options.Add(new() { Uid = Guid.NewGuid(), Version = 1, AttributeGroupId = group.Uid, OptionCode = "CEILING", OptionName = "吸顶" });
            _data.Groups.Add(group); _data.ProfileRelations.Add(new() { Uid = Guid.NewGuid(), Version = 3, ProductAttributeProfileId = categoryId, AttributeGroupId = group.Uid });
        }
        public SaveProductAttributeDefinitionsRequest? LastRequest { get; private set; }
        public int SaveCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public bool FailSave { get; set; }
        public bool IncompleteSnapshot { get; set; }
        public TaskCompletionSource<bool>? PauseRead { get; set; }
        public TaskCompletionSource<bool>? PauseSave { get; set; }
        /// <inheritdoc />
        public async Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        { ReadCalls++; if (PauseRead is not null) await PauseRead.Task; return ServiceResult<ProductAttributeDefinitionsDto>.Ok(_data.Adapt<ProductAttributeDefinitionsDto>()); }
        /// <inheritdoc />
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetProfileDefinitionsAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("独立标签页必须读取全局定义。");
        /// <inheritdoc />
        public async Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
        {
            SaveCalls++; LastRequest = request;
            if (PauseSave is not null) await PauseSave.Task;
            if (FailSave) return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail("定义仍被 1 个主型号引用。");
            var response = new SaveProductAttributeDefinitionsResponse(); var map = new Dictionary<Guid, Guid>();
            foreach (var item in request.GroupChanges.DeleteItems)
            {
                _data.Groups.RemoveAll(group => group.Uid == item.Uid); _data.ProfileRelations.RemoveAll(relation => relation.AttributeGroupId == item.Uid);
                response.GroupResults.Items.Add(Receipt(item, item.Uid, SaveOperation.Delete, item.Version, item.Version));
            }
            foreach (var item in request.GroupChanges.UpdateItems)
            {
                var group = _data.Groups.Single(group => group.Uid == item.Uid); Assert.AreEqual(group.Version, item.Version);
                item.Data.Adapt(group); group.Version++;
                response.GroupResults.Items.Add(Receipt(item, group.Uid, SaveOperation.Update, item.Version, group.Version));
            }
            foreach (var item in request.GroupChanges.CreateItems)
            {
                var group = item.Data.Adapt<ProductAttributeGroupDto>(); group.Uid = Guid.NewGuid(); group.Version = 1;
                map[item.Data.ClientUid] = group.Uid; _data.Groups.Add(group);
                response.GroupResults.Items.Add(Receipt(item, group.Uid, SaveOperation.Create, null, 1));
            }
            foreach (var item in request.OptionChanges.DeleteItems)
            { foreach (var group in _data.Groups) group.Options.RemoveAll(option => option.Uid == item.Uid); response.OptionResults.Items.Add(Receipt(item, item.Uid, SaveOperation.Delete, item.Version, item.Version)); }
            foreach (var item in request.OptionChanges.UpdateItems)
            {
                var option = _data.Groups.SelectMany(group => group.Options).Single(option => option.Uid == item.Uid); item.Data.Adapt(option); option.Version++;
                response.OptionResults.Items.Add(Receipt(item, option.Uid, SaveOperation.Update, item.Version, option.Version));
            }
            foreach (var item in request.OptionChanges.CreateItems)
            {
                var option = item.Data.Adapt<ProductAttributeOptionDto>(); option.Uid = Guid.NewGuid(); option.Version = 1;
                if (map.TryGetValue(option.AttributeGroupId, out var realId)) option.AttributeGroupId = realId;
                _data.Groups.Single(group => group.Uid == option.AttributeGroupId).Options.Add(option);
                response.OptionResults.Items.Add(Receipt(item, option.Uid, SaveOperation.Create, null, 1));
            }
            foreach (var item in request.ProfileChanges.DeleteItems)
            { _data.ProfileRelations.RemoveAll(relation => relation.Uid == item.Uid); response.ProfileResults.Items.Add(Receipt(item, item.Uid, SaveOperation.Delete, item.Version, item.Version)); }
            foreach (var item in request.ProfileChanges.CreateItems)
            {
                var relation = item.Data.Adapt<ProductAttributeProfileGroupDto>(); relation.Uid = Guid.NewGuid(); relation.Version = 1;
                if (map.TryGetValue(relation.AttributeGroupId, out var realId)) relation.AttributeGroupId = realId;
                _data.ProfileRelations.Add(relation); response.ProfileResults.Items.Add(Receipt(item, relation.Uid, SaveOperation.Create, null, 1));
            }
            response.Definitions = IncompleteSnapshot ? new() : _data.Adapt<ProductAttributeDefinitionsDto>();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(response);
        }
        private static SaveItemResult Receipt(SaveItemRequestBase item, Guid uid, SaveOperation operation, int? oldVersion, int version) =>
            new() { ClientKey = item.ClientKey, Success = true, Uid = uid, OldVersion = oldVersion, NewVersion = version, Operation = operation };
    }

    /// <summary>只响应页面需要的缓存入口和只读属性模板候选，失败仅影响后续共享缓存。</summary>
    private class CacheProxy : DispatchProxy
    {
        public IReadOnlyList<SysDictDataDto> Profiles { get; set; } = [];
        public bool RefreshSuccess { get; set; } = true;
        public int RefreshAttributeCalls { get; private set; }
        private EventHandler? _changed;
        /// <summary>模拟共享缓存完成一次字典快照替换。</summary>
        public void RaiseChanged() => _changed?.Invoke(this, EventArgs.Empty);
        /// <summary>显式提供页面依赖，未授权的其他缓存调用直接使测试失败。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "add_ProductAttributesChanged") { _changed += (EventHandler)args![0]!; return null; }
            if (method?.Name == "remove_ProductAttributesChanged") { _changed -= (EventHandler)args![0]!; return null; }
            if (method?.Name == nameof(IReferenceDataCacheService.GetDictionaryValues)) return Profiles;
            if (method?.Name == nameof(IReferenceDataCacheService.EnsureProductAttributesLoadedAsync)) return Task.FromResult(CacheRefreshResult.Ok());
            if (method?.Name == nameof(IReferenceDataCacheService.RefreshProductAttributesAsync))
            { RefreshAttributeCalls++; return Task.FromResult(RefreshSuccess ? CacheRefreshResult.Ok() : CacheRefreshResult.Fail("离线缓存读取失败。")); }
            throw new AssertFailedException("标签页不应读取其他缓存状态：" + method?.Name);
        }
    }
    /// <summary>只允许模板维护入口使用普通路由，避免误建主型号详情身份。</summary>
    private class NavigationProxy : DispatchProxy
    {
        public string? LastRoute { get; private set; }
        public string? LastDocumentKey { get; private set; }
        public object? LastParameter { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(INavigationService.OpenDocumentAsync)) throw new AssertFailedException(method?.Name);
            LastRoute = (string)args![0]!;
            LastDocumentKey = (string)args[1]!;
            LastParameter = args[2];
            return Task.CompletedTask;
        }
    }

    private sealed class Runner : IApiCallRunner
    {
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    private sealed class Messages : IMessageService
    {
        public List<string> Warnings { get; } = [];
        /// <inheritdoc />
        public void ShowInfo(string title, string message) { }
        /// <inheritdoc />
        public void ShowSuccess(string title, string message) { }
        /// <inheritdoc />
        public void ShowWarning(string title, string message) => Warnings.Add(title + message);
        /// <inheritdoc />
        public void ShowError(string title, string message) { }
    }
    private sealed class Dialogs : IDialogService
    {
        public DialogResult NextDecision { get; set; } = DialogResult.Secondary;
        /// <inheritdoc />
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default) => Task.FromResult(NextDecision);
        /// <inheritdoc />
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        /// <inheritdoc />
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <inheritdoc />
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}


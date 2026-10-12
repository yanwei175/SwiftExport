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
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Categories.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证分类标签编辑工作区及分类宿主的回执、草稿和写入隔离行为。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductAttributeDefinitionViewModelTests
{
    /// <summary>新组和新选项同批提交临时身份，成功后恢复真实组和版本；缓存失败不回滚提交。</summary>
    [TestMethod]
    public void Save_NewGroupAndOption_AcceptsServerIdentityEvenWhenCacheRefreshFails() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Cache.RefreshSuccess = false;
        await fixture.LoadAttributesAsync();
        fixture.Attributes.AddGroupCommand.Execute(null);
        var group = fixture.Attributes.SelectedGroup!;
        group.GroupCode = "MOUNT"; group.GroupName = "安装方式";
        fixture.Attributes.AddOptionCommand.Execute(null);
        fixture.Attributes.SelectedOption!.OptionCode = "CEILING"; fixture.Attributes.SelectedOption.OptionName = "吸顶";
        var temporaryUid = group.Uid;

        Assert.IsTrue(await fixture.Attributes.TrySaveAsync());

        var submitted = fixture.Api.LastRequest!;
        Assert.AreEqual(temporaryUid, submitted.GroupChanges.CreateItems.Single().Data.ClientUid);
        Assert.AreEqual(temporaryUid, submitted.OptionChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreNotEqual(temporaryUid, fixture.Attributes.SelectedGroup!.Uid);
        Assert.AreEqual(fixture.Attributes.SelectedGroup.Uid, fixture.Attributes.Options.Items.Single().AttributeGroupId);
        Assert.AreEqual(1, fixture.Attributes.SelectedGroup.Version);
        Assert.IsFalse(fixture.Attributes.HasUnsavedChanges);
        Assert.AreEqual(1, fixture.Cache.RefreshAttributeCalls);
        Assert.IsTrue(fixture.Messages.Warnings.Any(message => message.Contains("缓存")));
    });

    /// <summary>已引用定义删除被服务端拒绝后，组与子项仍保留删除池并可整体撤销。</summary>
    [TestMethod]
    public void Delete_Group_CascadesOnlyOnServerAndFailurePreservesDraft() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        await fixture.Attributes.DeleteGroupCommand.ExecuteAsync(null);
        var request = fixture.Attributes.BuildRequest();
        Assert.AreEqual(1, request.GroupChanges.DeleteItems.Count);
        Assert.AreEqual(0, request.OptionChanges.DeleteItems.Count);
        Assert.AreEqual(1, fixture.Attributes.Options.DeletedItems.Count);
        fixture.Api.FailSave = true;

        Assert.IsFalse(await fixture.Attributes.TrySaveAsync());
        Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
        Assert.AreEqual(1, fixture.Attributes.Groups.DeletedItems.Count);
        fixture.Attributes.RejectCommand.Execute(null);
        Assert.AreEqual(1, fixture.Attributes.Groups.Items.Count);
        Assert.AreEqual(1, fixture.Attributes.CurrentOptions.Cast<object>().Count());
        Assert.IsFalse(fixture.Attributes.HasUnsavedChanges);
    });

    /// <summary>切组仅切本地视图，不丢失前组选项编辑。</summary>
    [TestMethod]
    public void SwitchingGroup_PreservesOtherGroupOptionDraft() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        var first = fixture.Attributes.SelectedGroup!;
        var option = fixture.Attributes.SelectedOption!;
        option.OptionName = "挂墙";
        fixture.Attributes.AddGroupCommand.Execute(null);
        fixture.Attributes.SelectedGroup!.GroupCode = "SIZE";
        fixture.Attributes.SelectedGroup.GroupName = "尺寸";
        fixture.Attributes.AddOptionCommand.Execute(null);
        fixture.Attributes.SelectedOption!.OptionCode = "8";
        fixture.Attributes.SelectedOption.OptionName = "8寸";
        fixture.Attributes.SelectedGroup = first;
        Assert.AreEqual("挂墙", fixture.Attributes.SelectedOption!.OptionName);
        Assert.AreEqual(1, fixture.Attributes.Options.ModifiedItems.Count);
        Assert.AreEqual(1, fixture.Attributes.Options.AddedItems.Count);
        Assert.AreEqual(1, fixture.Attributes.BuildRequest().OptionChanges.UpdateItems.Count);
    });

    /// <summary>本地编码唯一性阻断保存，不把未编辑成功状态接纳。</summary>
    [TestMethod]
    public void DuplicateCodes_AreRejectedWithoutPosting() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        fixture.Attributes.AddGroupCommand.Execute(null);
        fixture.Attributes.SelectedGroup!.GroupCode = " mount ";
        fixture.Attributes.SelectedGroup.GroupName = "另一个组";
        Assert.IsFalse(await fixture.Attributes.TrySaveAsync());
        Assert.AreEqual(0, fixture.Api.SaveCalls);
        Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
        StringAssert.Contains(fixture.Attributes.ValidationSummary!, "重复");
    });

    /// <summary>基础定义可读，缺少分类编辑权限时不能新增或修改定义。</summary>
    [TestMethod]
    public void ReadWithoutEditPermission_LoadsDefinitionsButDisablesEditing() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true, canEdit: false);
        await fixture.LoadAttributesAsync();
        Assert.IsTrue(fixture.Attributes.IsLoaded);
        Assert.AreEqual(1, fixture.Attributes.Groups.Items.Count);
        Assert.IsFalse(fixture.Attributes.CanEditDefinitions);
        Assert.IsFalse(fixture.Attributes.AddGroupCommand.CanExecute(null));
        Assert.IsTrue(fixture.Attributes.AreGridsReadOnly);
    });

    /// <summary>分类有草稿或仍是临时新增记录时，定义维护不发送独立写入或读取。</summary>
    [TestMethod]
    public void CategoryDraftOrNewCategory_PreventsIndependentDefinitionWrites() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        await fixture.LoadAttributesAsync();
        fixture.Host.CurrentCategory!.CategoryName = "改名草稿";
        Assert.IsFalse(fixture.Attributes.CanEditDefinitions);
        await fixture.Host.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.Attributes.CanEditDefinitions);
        fixture.Host.AddRootCommand.Execute(null);
        var readCalls = fixture.Api.ReadCalls;
        await fixture.Host.ActivateSelectedTabAsync();
        Assert.IsFalse(fixture.Attributes.IsLoaded);
        Assert.IsFalse(fixture.Attributes.CanEditDefinitions);
        Assert.AreEqual(readCalls, fixture.Api.ReadCalls);
    });

    /// <summary>定义草稿取消切分类和关闭时，旧分类身份、选择和输入完整保留。</summary>
    [TestMethod]
    public void CancelCategorySwitchAndClose_PreservesOldContext() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        var original = fixture.Host.CurrentCategory!;
        fixture.Attributes.SelectedGroup!.GroupName = "待保存名称";
        fixture.Dialogs.NextDecision = DialogResult.Close;
        var next = fixture.Host.CategoryTree.Last();
        next.IsSelected = true;
        await fixture.Host.SelectCurrentCategoryAsync(next);
        Assert.AreSame(original, fixture.Host.CurrentCategory);
        Assert.AreEqual(original.Uid, fixture.Attributes.CategoryId);
        Assert.IsTrue(original.IsSelected);
        Assert.IsFalse(next.IsSelected);
        Assert.AreEqual("待保存名称", fixture.Attributes.SelectedGroup!.GroupName);
        Assert.IsFalse(await fixture.Host.CanCloseAsync(default));
    });

    /// <summary>新增分类保存后仍定位回执真实 Uid，标签读取指向真实分类而非首项。</summary>
    [TestMethod]
    public void CategorySave_RestoresNewlyPersistedCategoryForDefinitions() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        await fixture.Host.InitializeAsync();
        fixture.Host.AddRootCommand.Execute(null);
        fixture.Host.CurrentCategory!.CategoryName = "顶喷";
        var temporaryUid = fixture.Host.CurrentCategory.Uid;
        fixture.Host.SelectedTabIndex = 1;
        await fixture.Host.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual("顶喷", fixture.Host.CurrentCategory!.CategoryName);
        Assert.AreNotEqual(temporaryUid, fixture.Host.CurrentCategory.Uid);
        Assert.AreEqual(fixture.Host.CurrentCategory.Uid, fixture.Attributes.CategoryId);
        Assert.AreEqual(fixture.Host.CurrentCategory.Uid, fixture.Api.LastReadCategory);
        Assert.IsTrue(fixture.Attributes.IsLoaded);
        Assert.IsFalse(fixture.Host.HasUnsavedChanges);
    });

    /// <summary>标签正在提交时宿主拒绝关闭、切分类及新增分类，防止两个 Busy 相互穿透。</summary>
    [TestMethod]
    public void DefinitionWrite_FreezesHostUntilServerResponse() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        fixture.Attributes.SelectedGroup!.GroupName = "编辑中";
        var pause = fixture.Api.PauseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = fixture.Attributes.TrySaveAsync();
        Assert.IsTrue(fixture.Attributes.IsBusy);
        Assert.IsFalse(fixture.Host.IsInteractionEnabled);
        Assert.IsFalse(fixture.Host.AddRootCommand.CanExecute(null));
        Assert.IsFalse(await fixture.Host.CanCloseAsync(default));
        var original = fixture.Host.CurrentCategory;
        await fixture.Host.SelectCurrentCategoryAsync(fixture.Host.CategoryTree.Last());
        Assert.AreSame(original, fixture.Host.CurrentCategory);
        pause.SetResult(true);
        Assert.IsTrue(await saving);
        Assert.IsTrue(fixture.Host.IsInteractionEnabled);
    });

    /// <summary>已释放分类工作区不会接纳忽略取消的迟到定义响应。</summary>
    [TestMethod]
    public void Dispose_IgnoresLateDefinitionResult() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.Host.InitializeAsync();
        fixture.Api.PauseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SelectedTabIndex = 1;
        var loading = fixture.Host.ActivateSelectedTabAsync();
        fixture.Host.Dispose();
        fixture.Api.PauseRead.SetResult(true);
        await loading;
        Assert.IsFalse(fixture.Attributes.IsLoaded);
        Assert.AreEqual(0, fixture.Attributes.Groups.Items.Count);
    });

    /// <summary>快速切分类会取消旧读取，忽略取消的旧响应也不能覆盖新分类的空定义。</summary>
    [TestMethod]
    public void SwitchingCategory_IgnoresEarlierDefinitionRead() => StaTest.RunAsync(async () =>
    {
        using var fixture = new Fixture(withDefinition: true);
        await fixture.Host.InitializeAsync();
        var pause = fixture.Api.PauseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SelectedTabIndex = 1;
        var oldRead = fixture.Host.ActivateSelectedTabAsync();
        fixture.Api.PauseRead = null;
        var next = fixture.Host.CategoryTree.Last();
        await fixture.Host.SelectCurrentCategoryAsync(next);
        pause.SetResult(true);
        await oldRead;
        Assert.AreEqual(next.Uid, fixture.Attributes.CategoryId);
        Assert.IsTrue(fixture.Attributes.IsLoaded);
        Assert.AreEqual(0, fixture.Attributes.Groups.Items.Count);
        Assert.IsFalse(fixture.Attributes.IsLoading);
    });

    /// <summary>真实页面提交数值转换错误会被阻断，修正后可保存；取消切树恢复视觉和模型身份。</summary>
    [TestMethod]
    public void Views_CommitNativeGridEditAndRestoreCancelledTreeSelection() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture(withDefinition: true);
        await fixture.LoadAttributesAsync();
        var view = new ProductCategoryManagerView { DataContext = fixture.Host };
        var window = new Window { Content = view, Width = 1400, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            var child = (ProductAttributeDefinitionView)view.FindName("AttributeDefinitions");
            var grid = (DataGrid)child.FindName("GroupGrid");
            var optionGrid = (DataGrid)child.FindName("OptionGrid");
            Assert.AreEqual(5, grid.Columns.Count);
            Assert.AreEqual(4, optionGrid.Columns.Count);
            Assert.AreSame(fixture.Attributes.SelectedGroup, grid.SelectedItem);
            var group = fixture.Attributes.SelectedGroup!;
            grid.CurrentCell = new(group, grid.Columns[4]);
            Assert.IsTrue(grid.BeginEdit());
            await Dispatcher.Yield(DispatcherPriority.Background);
            var editor = (TextBox)grid.Columns[4].GetCellContent(group);
            editor.Text = "非法数字"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor));
            Assert.IsFalse(child.CommitPendingEdits());
            Assert.IsFalse(await fixture.Host.CanCloseAsync(default), "无效单元格即使尚未更新模型，也必须阻止关闭。");
            editor.Text = "8"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(child.CommitPendingEdits());
            Assert.AreEqual(8, group.SortOrder);
            Assert.IsTrue(fixture.Attributes.HasUnsavedChanges);
            fixture.Dialogs.NextDecision = DialogResult.Close;
            var tree = FindChild<TreeView>(view)!;
            var original = fixture.Host.CurrentCategory;
            var item = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(fixture.Host.CategoryTree.Last());
            item.IsSelected = true;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.AreSame(original, tree.SelectedItem);
            Assert.AreSame(original, fixture.Host.CurrentCategory);
            Assert.IsFalse(item.IsSelected);
            Assert.AreEqual(8, fixture.Attributes.SelectedGroup!.SortOrder);
        }
        finally { window.Close(); }
    });

    /// <summary>仅在测试中查找已实现的视觉容器，不在业务实现缓存或扫描 Grid 行。</summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>为测试提供无网络分类和定义工作区；所有状态对象只属于当前夹具。</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(bool withDefinition = false, bool canEdit = true)
        {
            var user = new CurrentUserService();
            user.SetCurrentUser(new MeDto { UserName = "test", Permissions = canEdit ? [PermissionKeys.ProductsCategoryEdit] : [] });
            var runner = new Runner();
            var categoryApi = new CategoryApi();
            Api = new AttributeApi(categoryApi.FirstId, withDefinition);
            var cacheService = DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>();
            Cache = (CacheProxy)(object)cacheService;
            Attributes = new(Api, runner, cacheService, Messages, Dialogs, user);
            Host = new(categoryApi, runner, Messages, Dialogs, cacheService,
                new ProductCategoryAssignmentViewModel(DispatchProxy.Create<IProductMasterApi, NoCalls>(), runner, Messages, Dialogs), Attributes);
        }
        public AttributeApi Api { get; }
        public CacheProxy Cache { get; }
        public Messages Messages { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ProductAttributeDefinitionViewModel Attributes { get; }
        public ProductCategoryManagerViewModel Host { get; }
        public async Task LoadAttributesAsync()
        {
            await Host.InitializeAsync();
            Host.SelectedTabIndex = 1;
            await Host.ActivateSelectedTabAsync();
        }
        public void Dispose() => Host.Dispose();
    }

    /// <summary>离线定义服务替身，生成与后端协议相同的父子映射回执与完整快照。</summary>
    private sealed class AttributeApi(Guid categoryId, bool withDefinition) : IProductAttributeApi
    {
        private readonly Dictionary<Guid, ProductAttributeDefinitionsDto> _data = new();
        public SaveProductAttributeDefinitionsRequest? LastRequest { get; private set; }
        public int SaveCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public Guid LastReadCategory { get; private set; }
        public bool FailSave { get; set; }
        public TaskCompletionSource<bool>? PauseRead { get; set; }
        public TaskCompletionSource<bool>? PauseSave { get; set; }
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new ProductAttributeDefinitionsDto()));
        public async Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ReadCalls++; LastReadCategory = id;
            if (PauseRead is not null) await PauseRead.Task;
            if (!_data.TryGetValue(id, out var result))
            {
                result = new() { CategoryId = id };
                if (withDefinition && id == categoryId)
                {
                    var group = new ProductAttributeGroupDto { Uid = Guid.NewGuid(), Version = 1, CategoryId = id, GroupCode = "MOUNT", GroupName = "安装方式" };
                    group.Options.Add(new() { Uid = Guid.NewGuid(), Version = 1, AttributeGroupId = group.Uid, OptionCode = "CEILING", OptionName = "吸顶" });
                    result.Groups.Add(group);
                }
                _data[id] = result;
            }
            return ServiceResult<ProductAttributeDefinitionsDto>.Ok(result.Adapt<ProductAttributeDefinitionsDto>());
        }
        public async Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid id, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
        {
            SaveCalls++; LastRequest = request;
            if (PauseSave is not null) await PauseSave.Task;
            if (FailSave) return ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail("定义仍被 1 个主型号引用。");
            var snapshot = _data[id];
            var response = new SaveProductAttributeDefinitionsResponse();
            var map = new Dictionary<Guid, Guid>();
            foreach (var item in request.GroupChanges.DeleteItems)
            { snapshot.Groups.RemoveAll(group => group.Uid == item.Uid); response.GroupResults.Items.Add(Receipt(item, item.Uid, SaveOperation.Delete, item.Version + 1)); }
            foreach (var item in request.GroupChanges.UpdateItems)
            {
                var group = snapshot.Groups.Single(group => group.Uid == item.Uid);
                item.Data.Adapt(group); group.Version++;
                response.GroupResults.Items.Add(Receipt(item, group.Uid, SaveOperation.Update, group.Version));
            }
            foreach (var item in request.GroupChanges.CreateItems)
            {
                var group = item.Data.Adapt<ProductAttributeGroupDto>(); group.Uid = Guid.NewGuid(); group.Version = 1; group.CategoryId = id;
                map[item.Data.ClientUid] = group.Uid; snapshot.Groups.Add(group);
                response.GroupResults.Items.Add(Receipt(item, group.Uid, SaveOperation.Create, 1));
            }
            foreach (var item in request.OptionChanges.DeleteItems)
            { foreach (var group in snapshot.Groups) group.Options.RemoveAll(option => option.Uid == item.Uid); response.OptionResults.Items.Add(Receipt(item, item.Uid, SaveOperation.Delete, item.Version + 1)); }
            foreach (var item in request.OptionChanges.UpdateItems)
            {
                var option = snapshot.Groups.SelectMany(group => group.Options).Single(option => option.Uid == item.Uid);
                item.Data.Adapt(option); option.Version++;
                response.OptionResults.Items.Add(Receipt(item, option.Uid, SaveOperation.Update, option.Version));
            }
            foreach (var item in request.OptionChanges.CreateItems)
            {
                var option = item.Data.Adapt<ProductAttributeOptionDto>(); option.Uid = Guid.NewGuid(); option.Version = 1;
                if (map.TryGetValue(option.AttributeGroupId, out var realId)) option.AttributeGroupId = realId;
                snapshot.Groups.Single(group => group.Uid == option.AttributeGroupId).Options.Add(option);
                response.OptionResults.Items.Add(Receipt(item, option.Uid, SaveOperation.Create, 1));
            }
            response.Definitions = snapshot.Adapt<ProductAttributeDefinitionsDto>();
            return ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(response);
        }
        private static SaveItemResult Receipt(SaveItemRequestBase item, Guid uid, SaveOperation operation, int version) =>
            new() { ClientKey = item.ClientKey, Success = true, Uid = uid, NewVersion = version, Operation = operation };
    }

    /// <summary>分类 API 替身；新分类必须返回不同的真实 Uid。</summary>
    private sealed class CategoryApi : IProductCategoryApi
    {
        public Guid FirstId { get; } = Guid.NewGuid();
        private List<ProductCategoryDto>? _categories;
        public Task<ServiceResult<List<ProductCategoryDto>>> GetAllAsync()
        {
            _categories ??= [new() { Uid = FirstId, Version = 1, CategoryName = "初始分类", SortNo = 10 },
                new() { Uid = Guid.NewGuid(), Version = 1, CategoryName = "另一个分类", SortNo = 20 }];
            return Task.FromResult(ServiceResult<List<ProductCategoryDto>>.Ok(_categories.Adapt<List<ProductCategoryDto>>()));
        }
        public Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest request)
        {
            var response = new ProductCategoryBatchSaveResponse();
            foreach (var item in request.CreateItems)
            {
                var dto = item.Data.Adapt<ProductCategoryDto>(); dto.Uid = Guid.NewGuid(); dto.Version = 1; _categories!.Add(dto);
                response.Items.Add(new() { ClientKey = item.ClientKey, Success = true, Uid = dto.Uid, NewVersion = 1, Operation = SaveOperation.Create });
            }
            foreach (var item in request.UpdateItems)
            {
                var dto = _categories!.Single(dto => dto.Uid == item.Uid); item.Data.Adapt(dto); dto.Version++;
                response.Items.Add(new() { ClientKey = item.ClientKey, Success = true, Uid = dto.Uid, NewVersion = dto.Version, Operation = SaveOperation.Update });
            }
            return Task.FromResult(ServiceResult<ProductCategoryBatchSaveResponse>.Ok(response));
        }
        public Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    /// <summary>缓存测试代理只响应本页面的独立刷新，不共享其他测试可变缓存。</summary>
    private class CacheProxy : DispatchProxy
    {
        public bool RefreshSuccess { get; set; } = true;
        public int RefreshAttributeCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IReferenceDataCacheService.RefreshProductAttributesAsync)) RefreshAttributeCalls++;
            return Task.FromResult(RefreshSuccess ? CacheRefreshResult.Ok() : CacheRefreshResult.Fail("离线缓存读取失败。"));
        }
    }

    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    private sealed class Messages : IMessageService
    {
        public List<string> Warnings { get; } = [];
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) => Warnings.Add(title + message);
        public void ShowError(string title, string message) { }
    }
    private sealed class Dialogs : IDialogService
    {
        public DialogResult NextDecision { get; set; } = DialogResult.Secondary;
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default) => Task.FromResult(NextDecision);
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private class NoCalls : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new AssertFailedException("不应调用主型号 API。");
    }
}


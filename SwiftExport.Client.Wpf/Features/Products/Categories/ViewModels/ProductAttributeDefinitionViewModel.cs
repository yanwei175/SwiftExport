using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Categories.ViewModels;

/// <summary>维护一个分类的标签组和选项草稿，以一次原子保存提交两张表。</summary>
public sealed partial class ProductAttributeDefinitionViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IProductAttributeApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private bool _disposed;
    private bool _hostBusy;
    private bool _hostHasChanges;
    private bool _hostCanWrite = true;
    private bool _isPersistedCategory;

    /// <summary>注入定义 API、会话缓存和宿主无关的反馈依赖；编辑数据始终由本实例持有。</summary>
    public ProductAttributeDefinitionViewModel(IProductAttributeApi api, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs, ICurrentUserService user)
    {
        _api = api; _runner = runner; _cache = cache; _messages = messages; _dialogs = dialogs; _user = user;
        Groups = new(dto => new ProductAttributeGroupLineModel());
        Options = new(dto => new ProductAttributeOptionLineModel());
        CurrentOptions = CollectionViewSource.GetDefaultView(Options.Items);
        CurrentOptions.Filter = item => item is ProductAttributeOptionLineModel option && option.AttributeGroupId == SelectedGroup?.Uid;
        Groups.ListStateChanged += OnGroupStateChanged;
        Options.ListStateChanged += OnOptionStateChanged;
        PropertyChanged += OnOwnStateChanged;
        _user.CurrentUserChanged += OnPermissionsChanged;
        foreach (var command in new IRelayCommand[] { AddGroupCommand, DeleteGroupCommand, AddOptionCommand,
            DeleteOptionCommand, SaveCommand, RefreshCommand, RejectCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>当前分类全部组的编辑状态及删除池。</summary>
    public BaseListModel<ProductAttributeGroupLineModel, ProductAttributeGroupDto> Groups { get; }
    /// <summary>当前分类全部组选项的编辑状态；切组不会覆盖其他组草稿。</summary>
    public BaseListModel<ProductAttributeOptionLineModel, ProductAttributeOptionDto> Options { get; }
    /// <summary>仅显示选中组的选项，仍共享本页面的私有全量工作区。</summary>
    public ICollectionView CurrentOptions { get; }
    /// <summary>提供中文展示和稳定枚举值的选择方式候选。</summary>
    public IReadOnlyList<KeyValuePair<ProductAttributeSelectionMode, string>> SelectionModes { get; } =
        [new(ProductAttributeSelectionMode.Single, "单选"), new(ProductAttributeSelectionMode.Multiple, "多选")];
    /// <summary>视觉层提交当前单元格的窄回调；无 View 宿主的模型测试不需要提供。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }

    [ObservableProperty] public partial Guid CategoryId { get; private set; }
    [ObservableProperty] public partial string CategoryName { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool RequiresReload { get; private set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "请选择一个已保存分类。";
    [ObservableProperty] public partial ProductAttributeGroupLineModel? SelectedGroup { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteOptionCommand))]
    public partial ProductAttributeOptionLineModel? SelectedOption { get; set; }

    /// <summary>真实未保存状态，包含不再显示的已保存删除行。</summary>
    public bool HasUnsavedChanges => Groups.HasChanges || Options.HasChanges;
    /// <summary>删除池仍参与未保存提示；删除行已从表格移除，但不会隐去待提交影响。</summary>
    public string PendingChangeSummary => HasUnsavedChanges
        ? $"待保存：新增组 {Groups.AddedItems.Count}、修改组 {Groups.ModifiedItems.Count}、删除组 {Groups.DeletedItems.Count}；新增值 {Options.AddedItems.Count}、修改值 {Options.ModifiedItems.Count}、删除值 {Options.DeletedItems.Count}。"
        : string.Empty;
    /// <summary>共同 Busy 门禁阻止子页写入期间的宿主选择和编辑。</summary>
    public bool IsInteractionEnabled => !_disposed && IsNotBusy && !_hostBusy && !IsLoading;
    /// <summary>写入需要分类已保存、树无草稿及现有分类编辑权限。</summary>
    public bool CanEditDefinitions => IsInteractionEnabled && IsLoaded && !RequiresReload
        && _isPersistedCategory && !_hostHasChanges && _hostCanWrite && _user.HasPermission(PermissionKeys.ProductsCategoryEdit);
    /// <summary>字段只读能力供 DataGrid 绑定；已登录读取不依赖编辑权限。</summary>
    public bool AreGridsReadOnly => !CanEditDefinitions;
    /// <summary>解释新增分类、树草稿、加载及权限等写入限制。</summary>
    public string HostStateMessage => !_isPersistedCategory ? "请先保存分类，再维护标签定义。" :
        _hostHasChanges ? "分类有未保存修改，请先保存或放弃分类。" :
        !_user.HasPermission(PermissionKeys.ProductsCategoryEdit) || !_hostCanWrite ? "当前会话无权编辑标签定义。" :
        RequiresReload ? "保存回执不完整，请刷新核对服务器结果后继续。" :
        IsLoading ? "正在加载当前分类标签定义。" :
        !IsLoaded ? "尚未成功加载当前分类标签定义。" : string.Empty;

    /// <summary>接收宿主窄状态，不反向读取或修改分类树。</summary>
    public void SetHostState(Guid categoryId, string categoryName, bool persisted, bool hasChanges, bool canWrite, bool busy)
    {
        if (CategoryId != categoryId)
        {
            CancelLoad();
            CategoryId = categoryId;
            Groups.Clear(); Options.Clear();
            SelectedGroup = null; SelectedOption = null;
            IsLoaded = false; RequiresReload = false; ValidationSummary = null;
        }
        CategoryName = categoryName; _isPersistedCategory = persisted; _hostHasChanges = hasChanges;
        _hostCanWrite = canWrite; _hostBusy = busy;
        NotifyState();
    }

    /// <summary>标签页首次展示时读取当前分类最新定义；新增分类不会发出请求。</summary>
    public Task EnsureLoadedAsync() => IsLoaded || !_isPersistedCategory || _hostBusy || _disposed
        ? Task.CompletedTask : LoadAsync();

    /// <summary>宿主切分类、刷新、关闭前统一处理保存、放弃、取消离开。</summary>
    public async Task<bool> ResolveUnsavedChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy || _hostBusy) return false;
        if (CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges) return true;
        var decision = await _dialogs.ShowAsync("当前分类的标签定义有未保存修改。",
            new DialogOptions("标签定义未保存")
            { PrimaryButtonText = "保存", SecondaryButtonText = "放弃", CloseButtonText = "取消" },
            cancellationToken: cancellationToken);
        if (_disposed) return false;
        if (decision == DialogResult.Primary) return await TrySaveAsync();
        if (decision != DialogResult.Secondary) return false;
        RejectChangesCore();
        return true;
    }

    partial void OnSelectedGroupChanged(ProductAttributeGroupLineModel? value)
    {
        Groups.SelectedItem = value;
        CurrentOptions.Refresh();
        SelectedOption = CurrentOptions.Cast<ProductAttributeOptionLineModel>().FirstOrDefault();
        AddOptionCommand.NotifyCanExecuteChanged(); DeleteGroupCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddGroup()
    {
        if (!CanEdit()) return;
        SelectedGroup = Groups.AddNew(new ProductAttributeGroupDto
        { Uid = Guid.NewGuid(), CategoryId = CategoryId, SelectionMode = ProductAttributeSelectionMode.Single });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    private async Task DeleteGroupAsync()
    {
        if (!CanDeleteGroup() || SelectedGroup is not { } group) return;
        if (!group.IsNew && !await _dialogs.ConfirmAsync("删除标签组", "删除组时将同时删除其未被使用的标签值，保存后生效。", "删除")) return;
        if (!CanDeleteGroup()) return;
        using (Groups.SuppressListEvents())
        using (Options.SuppressListEvents())
        {
            foreach (var option in Options.AllLines.Where(item => item.AttributeGroupId == group.Uid).ToArray())
            {
                if (option.IsNew) Options.RemoveCompletely(option);
                else Options.Remove(option);
            }
            Groups.Remove(group);
        }
        SelectedGroup = Groups.Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanAddOption))]
    private void AddOption()
    {
        if (!CanAddOption()) return;
        SelectedOption = Options.AddNew(new ProductAttributeOptionDto { Uid = Guid.NewGuid(), AttributeGroupId = SelectedGroup!.Uid });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteOption))]
    private async Task DeleteOptionAsync()
    {
        if (!CanDeleteOption() || SelectedOption is not { } option) return;
        if (!option.IsNew && !await _dialogs.ConfirmAsync("删除标签值", "删除已被主型号使用的标签值会被服务器拒绝，保存后生效。", "删除")) return;
        if (!CanDeleteOption()) return;
        Options.Remove(option);
        SelectedOption = CurrentOptions.Cast<ProductAttributeOptionLineModel>().FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => TrySaveAsync();

    /// <summary>为保存按钮和离开确认提供同一提交入口，成功才接纳完整服务端快照。</summary>
    public async Task<bool> TrySaveAsync()
    {
        if (!CanSave()) return false;
        if (CommitPendingEdits?.Invoke() == false) return false;
        var saved = false;
        await RunWriteAsync(async () =>
        {
            var error = await Groups.ValidateAllAsync() ?? await Options.ValidateAllAsync();
            error ??= Groups.Items.GroupBy(item => item.GroupCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1) ? "同一分类的标签组编码不能重复。" : null;
            error ??= Options.Items.GroupBy(item => (item.AttributeGroupId, item.OptionCode.Trim().ToUpperInvariant()))
                .Any(group => group.Count() > 1) ? "同一标签组的标签值编码不能重复。" : null;
            if (error is not null) { ValidationSummary = error; StatusMessage = error; return; }
            var request = BuildRequest();
            var selectedUid = SelectedGroup?.Uid;
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(CategoryId, request), "保存标签定义失败");
            if (_disposed) return;
            if (!result.Success)
            {
                ValidationSummary = result.Message; StatusMessage = result.Message;
                _messages.ShowError("保存标签定义失败", result.Message); return;
            }
            var response = result.Data;
            if (response is null || !ValidReceipts(response.GroupResults, request.GroupChanges)
                || !ValidReceipts(response.OptionResults, request.OptionChanges)
                || !ValidSnapshot(response.Definitions))
            {
                RequiresReload = true;
                ValidationSummary = StatusMessage = "服务器已返回成功，但回执或完整定义不可用；草稿已保留，请刷新核对结果。";
                NotifyState(); return;
            }
            var newSelectedUid = request.GroupChanges.CreateItems.Any(item => item.Data.ClientUid == selectedUid)
                ? response.GroupResults.Items.FirstOrDefault(item => item.ClientKey == selectedUid?.ToString("N"))?.Uid
                : selectedUid;
            LoadSnapshot(response.Definitions, newSelectedUid);
            saved = true; StatusMessage = "标签定义已保存。";
            _messages.ShowSuccess("标签定义已保存", "标签组与标签值已一起提交。");
            var refresh = await _cache.RefreshProductAttributesAsync();
            if (!_disposed && !refresh.Success)
            {
                StatusMessage = "标签定义已保存，但缓存刷新失败；可稍后重新刷新。";
                _messages.ShowWarning("标签定义已保存，但缓存刷新失败", refresh.Message);
            }
        }, "正在保存标签定义...");
        return saved;
    }

    /// <summary>冻结两张表的变更集，已删除组的子选项由后端级联处理，不重复提交。</summary>
    public SaveProductAttributeDefinitionsRequest BuildRequest()
    {
        var deletedGroups = Groups.DeletedItems.Select(item => item.Uid).ToHashSet();
        return new SaveProductAttributeDefinitionsRequest
        {
            GroupChanges = new BatchSaveRequestBase<ProductAttributeGroupWriteDto>
            {
                CreateItems = Groups.AddedItems.Select(item => new CreateItemRequest<ProductAttributeGroupWriteDto>
                    { ClientKey = item.Uid.ToString("N"), Data = item.Adapt<ProductAttributeGroupWriteDto>() }).ToList(),
                UpdateItems = Groups.ModifiedItems.Select(item => new UpdateItemRequest<ProductAttributeGroupWriteDto>
                    { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version, Data = item.Adapt<ProductAttributeGroupWriteDto>() }).ToList(),
                DeleteItems = Groups.DeletedItems.Select(item => new DeleteItemRequest
                    { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version }).ToList()
            },
            OptionChanges = new BatchSaveRequestBase<ProductAttributeOptionWriteDto>
            {
                CreateItems = Options.AddedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId))
                    .Select(item => new CreateItemRequest<ProductAttributeOptionWriteDto>
                    { ClientKey = item.Uid.ToString("N"), Data = item.Adapt<ProductAttributeOptionWriteDto>() }).ToList(),
                UpdateItems = Options.ModifiedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId))
                    .Select(item => new UpdateItemRequest<ProductAttributeOptionWriteDto>
                    { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version, Data = item.Adapt<ProductAttributeOptionWriteDto>() }).ToList(),
                DeleteItems = Options.DeletedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId))
                    .Select(item => new DeleteItemRequest
                    { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version }).ToList()
            }
        };
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh() || !await ResolveUnsavedChangesAsync()) return;
        await LoadAsync();
        if (IsLoaded && !_disposed)
        {
            var result = await _cache.RefreshProductAttributesAsync();
            if (!result.Success && !_disposed) _messages.ShowWarning("标签缓存刷新失败", result.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject() => RejectChangesCore();

    /// <summary>只回滚标签工作区，不操作分类树或共享缓存。</summary>
    private void RejectChangesCore()
    {
        Groups.RejectChanges(); Options.RejectChanges();
        SelectedGroup = Groups.Items.FirstOrDefault();
        ValidationSummary = null; StatusMessage = "标签定义编辑已撤销。"; NotifyState();
    }

    /// <summary>查询同时检查取消、代际及分类身份；晚到请求不能覆盖新分类。</summary>
    private async Task LoadAsync()
    {
        if (_disposed || !_isPersistedCategory || _hostBusy || IsBusy) return;
        CancelLoad();
        var cts = _loadCts = new CancellationTokenSource();
        var generation = ++_loadGeneration;
        var categoryId = CategoryId;
        IsLoading = true;
        NotifyState();
        try
        {
            var result = await _runner.RunAsync(() => _api.GetCategoryDefinitionsAsync(categoryId, cts.Token), cts.Token, "加载标签定义失败");
            if (_disposed || cts.IsCancellationRequested || generation != _loadGeneration || CategoryId != categoryId) return;
            if (!result.Success || result.Data is null || !ValidSnapshot(result.Data))
            { ValidationSummary = StatusMessage = result.Success ? "服务器未返回完整标签定义。" : result.Message; return; }
            LoadSnapshot(result.Data, SelectedGroup?.Uid);
            StatusMessage = Groups.Items.Count == 0 ? "当前分类尚无标签定义，可新增标签组。" : "标签定义已加载。";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && generation == _loadGeneration && !cts.IsCancellationRequested)
                ValidationSummary = StatusMessage = "加载标签定义失败，请稍后重试。";
        }
        finally
        {
            if (generation == _loadGeneration)
            { IsLoading = false; NotifyState(); }
            if (ReferenceEquals(_loadCts, cts)) { _loadCts = null; cts.Dispose(); }
        }
    }

    /// <summary>加载和保存统一验证分类身份、真实 Uid、版本及选项父组，区分空快照和失败。</summary>
    private bool ValidSnapshot(ProductAttributeDefinitionsDto? definitions) =>
        definitions is not null && definitions.CategoryId == CategoryId && definitions.Groups is not null
        && definitions.Groups.Select(group => group.Uid).Distinct().Count() == definitions.Groups.Count
        && definitions.Groups.All(group => group.Uid != Guid.Empty && group.CategoryId == CategoryId
            && group.Version > 0 && group.Options is not null
            && group.Options.All(option => option.Uid != Guid.Empty && option.AttributeGroupId == group.Uid && option.Version > 0));

    /// <summary>逐项核对冻结请求与回执的客户端键、操作和身份，避免不完整成功误接纳草稿。</summary>
    private static bool ValidReceipts<T>(BatchSaveResponseBase? result, BatchSaveRequestBase<T> request)
    {
        if (result is null || result.Items is null || result.Items.Count != request.TotalCount || !result.AllSucceeded
            || result.Items.Any(item => item.Uid is null || item.Uid == Guid.Empty || item.NewVersion is null or <= 0)) return false;
        foreach (var item in request.CreateItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Create) != 1) return false;
        foreach (var item in request.UpdateItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Update
                && receipt.Uid == item.Uid && receipt.NewVersion > item.Version) != 1) return false;
        foreach (var item in request.DeleteItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Delete
                && receipt.Uid == item.Uid && receipt.NewVersion > item.Version) != 1) return false;
        return true;
    }

    /// <summary>回执确认后复制最新快照为私有编辑基线，并用真实 Uid 恢复当前组。</summary>
    private void LoadSnapshot(ProductAttributeDefinitionsDto definitions, Guid? selectedUid)
    {
        Groups.LoadFromDtos(definitions.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));
        Options.LoadFromDtos(definitions.Groups.SelectMany(item => item.Options).OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));
        SelectedGroup = Groups.Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Groups.Items.FirstOrDefault();
        IsLoaded = true; RequiresReload = false; ValidationSummary = null; NotifyState();
    }

    private void OnGroupStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeGroupLineModel> args) => NotifyState();
    private void OnOptionStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeOptionLineModel> args) => NotifyState();
    private void OnPermissionsChanged(object? sender, EventArgs args) => NotifyState();
    private void OnOwnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) NotifyState();
    }

    private void NotifyState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(IsInteractionEnabled));
        OnPropertyChanged(nameof(PendingChangeSummary));
        OnPropertyChanged(nameof(CanEditDefinitions)); OnPropertyChanged(nameof(AreGridsReadOnly)); OnPropertyChanged(nameof(HostStateMessage));
        AddGroupCommand.NotifyCanExecuteChanged(); DeleteGroupCommand.NotifyCanExecuteChanged();
        AddOptionCommand.NotifyCanExecuteChanged(); DeleteOptionCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged();
    }

    private bool CanEdit() => CanEditDefinitions;
    private bool CanAddOption() => CanEditDefinitions && SelectedGroup is not null;
    private bool CanDeleteGroup() => CanEditDefinitions && SelectedGroup is not null;
    private bool CanDeleteOption() => CanEditDefinitions && SelectedOption is not null;
    private bool CanSave() => CanEditDefinitions && HasUnsavedChanges;
    private bool CanRefresh() => IsInteractionEnabled && _isPersistedCategory;
    private bool CanReject() => IsInteractionEnabled && HasUnsavedChanges;

    private void CancelLoad()
    {
        _loadGeneration++;
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;
        IsLoading = false;
    }

    /// <summary>结束宿主生命周期时取消读取、丢弃晚到结果并解除所有订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelLoad();
        Groups.ListStateChanged -= OnGroupStateChanged; Options.ListStateChanged -= OnOptionStateChanged;
        PropertyChanged -= OnOwnStateChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
    }
}


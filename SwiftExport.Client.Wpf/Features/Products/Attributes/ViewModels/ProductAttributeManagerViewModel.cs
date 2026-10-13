using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Attributes.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;

namespace SwiftExportClient.Features.Products.Attributes.ViewModels;

/// <summary>独立维护全局标签组、值和直接分类关系，整页草稿只在原子保存成功后接纳。</summary>
public sealed partial class ProductAttributeManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>产品标签工作区的稳定单实例路由身份。</summary>
    public const string RouteKey = "product-attributes";
    private readonly IProductAttributeApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private readonly string? _sessionUserId;
    private readonly Dictionary<Guid, ProductAttributeCategorySelectionModel> _categorySelections = [];
    private readonly ObservableCollection<ProductAttributeCategoryCandidate> _categoryCandidates = [];
    private ProductAttributeDefinitionsDto _baseline = new();
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private bool _disposed, _sessionEnded;

    /// <summary>使用现有定义 API、缓存及反馈依赖；候选和勾选对象均复制为页面私有模型。</summary>
    public ProductAttributeManagerViewModel(IProductAttributeApi api, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs, ICurrentUserService user)
    {
        _api = api; _runner = runner; _cache = cache; _messages = messages; _dialogs = dialogs; _user = user;
        _sessionUserId = user.CurrentUser?.UserId;
        Title = "产品标签";
        IsReadOnly = !user.HasPermission(PermissionKeys.ProductsCategoryEdit);
        Groups = new(dto => new ProductAttributeGroupLineModel());
        Options = new(dto => new ProductAttributeOptionLineModel());
        CategoryCandidates = new(_categoryCandidates);
        CurrentOptions = CollectionViewSource.GetDefaultView(Options.Items);
        CurrentOptions.Filter = item => item is ProductAttributeOptionLineModel option && option.AttributeGroupId == SelectedGroup?.Uid;
        Groups.ListStateChanged += OnGroupStateChanged; Options.ListStateChanged += OnOptionStateChanged;
        PropertyChanged += OnOwnStateChanged; _user.CurrentUserChanged += OnPermissionsChanged;
        foreach (var command in new IRelayCommand[] { AddGroupCommand, DeleteGroupCommand, AddOptionCommand,
            DeleteOptionCommand, SaveCommand, RefreshCommand, RejectCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>全局共享组的私有编辑状态及删除池。</summary>
    public BaseListModel<ProductAttributeGroupLineModel, ProductAttributeGroupDto> Groups { get; }
    /// <summary>全局选项工作区；切组只切显示范围，不替换草稿。</summary>
    public BaseListModel<ProductAttributeOptionLineModel, ProductAttributeOptionDto> Options { get; }
    /// <summary>仅展示选中组的选项，保留其余组选项编辑。</summary>
    public ICollectionView CurrentOptions { get; }
    /// <summary>完整路径分类候选；缺失的既有关联仍保留可见占位，不能静默丢弃。</summary>
    public ReadOnlyObservableCollection<ProductAttributeCategoryCandidate> CategoryCandidates { get; }
    /// <summary>单选、多选标签规则候选。</summary>
    public IReadOnlyList<KeyValuePair<ProductAttributeSelectionMode, string>> SelectionModes { get; } =
        [new(ProductAttributeSelectionMode.Single, "单选"), new(ProductAttributeSelectionMode.Multiple, "多选")];
    /// <summary>视觉层提交原生单元格编辑并关闭未确认弹出层的窄回调。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>工作区单实例标题由导航注册路由负责去重。</summary>
    public string DocumentTitle => Title;

    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool IsResolvingChanges { get; private set; }
    [ObservableProperty] public partial bool RequiresReload { get; private set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "尚未读取产品标签。";
    [ObservableProperty] public partial ProductAttributeGroupLineModel? SelectedGroup { get; set; }
    [ObservableProperty] public partial ProductAttributeCategorySelectionModel? SelectedCategorySelection { get; private set; }
    [ObservableProperty] public partial int SelectedTabIndex { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteOptionCommand))]
    public partial ProductAttributeOptionLineModel? SelectedOption { get; set; }

    /// <summary>真实草稿包括组和值删除池，以及仍在工作区中的直接关系差异。</summary>
    public bool HasUnsavedChanges => Groups.HasChanges || Options.HasChanges || Groups.Items.Any(group => _categorySelections.TryGetValue(group.Uid, out var selection) && selection.HasChanges);
    /// <summary>显示整页待保存影响，删除行不再可见时仍明确提示。</summary>
    public string PendingChangeSummary => HasUnsavedChanges
        ? $"待保存：新增组 {Groups.AddedItems.Count}、修改组 {Groups.ModifiedItems.Count}、删除组 {Groups.DeletedItems.Count}；新增值 {Options.AddedItems.Count}、修改值 {Options.ModifiedItems.Count}、删除值 {Options.DeletedItems.Count}；分类关联修改 {_categorySelections.Count(pair => Groups.Items.Any(group => group.Uid == pair.Key) && pair.Value.HasChanges)} 组。"
        : string.Empty;
    /// <summary>当前会话有效且无读写或离开确认时才能交互。</summary>
    public bool IsInteractionEnabled => IsCurrentSession && IsNotBusy && !IsLoading && !IsResolvingChanges;
    /// <summary>写权限、完整服务器基线及运行状态共同决定字段编辑能力。</summary>
    public bool CanEditDefinitions => IsInteractionEnabled && IsLoaded && !RequiresReload && CanWrite;
    /// <summary>组选中后才允许操作对应的选项与分类关系。</summary>
    public bool CanEditCurrentGroup => CanEditDefinitions && SelectedGroup is not null;
    /// <summary>DataGrid 保持只读展示，实际写入由页面和后端权限共同保护。</summary>
    public bool AreGridsReadOnly => !CanEditDefinitions;
    /// <summary>说明会话、权限及回执不明导致的编辑限制。</summary>
    public string EditStateMessage => !IsCurrentSession ? "登录会话已结束，请重新打开页面。" :
        !CanWrite ? "当前会话无权编辑产品标签。" :
        RequiresReload ? "保存结果需要核对，请刷新服务器数据后继续。" :
        IsLoading ? "正在读取标签定义和分类候选。" :
        !IsLoaded ? "尚未成功读取完整标签定义。" : string.Empty;
    private bool IsCurrentSession => !_disposed && !_sessionEnded && _user.IsAuthenticated && _user.CurrentUser?.UserId == _sessionUserId;

    /// <summary>首次进入按需读取全局最新定义，重复激活保留当前草稿。</summary>
    public Task InitializeAsync() => EnsureLoadedAsync();
    /// <summary>成功的空快照同样视为已加载，不因切组或重复打开再次调用 API。</summary>
    public Task EnsureLoadedAsync() => IsLoaded || IsLoading || !IsCurrentSession ? Task.CompletedTask : LoadAsync(false);
    /// <summary>关闭页签时在途写入不可关闭，查询可随释放取消，草稿统一处理。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => ResolveUnsavedChangesAsync(cancellationToken);

    /// <summary>刷新和关闭统一提交当前单元格，再提供保存、放弃、取消选择。</summary>
    public async Task<bool> ResolveUnsavedChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy || IsResolvingChanges || CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges) return true;
        DialogResult decision;
        IsResolvingChanges = true; NotifyState();
        try
        {
            decision = await _dialogs.ShowAsync("产品标签有未保存修改，保存将提交整页的标签组、值和分类关联。",
                new DialogOptions("产品标签未保存") { PrimaryButtonText = "保存", SecondaryButtonText = "放弃", CloseButtonText = "取消" },
                cancellationToken: cancellationToken);
        }
        finally { IsResolvingChanges = false; NotifyState(); }
        if (_disposed) return false;
        if (decision == DialogResult.Primary) return await TrySaveAsync();
        if (decision != DialogResult.Secondary) return false;
        RejectChangesCore(); return true;
    }

    partial void OnSelectedGroupChanged(ProductAttributeGroupLineModel? value)
    {
        Groups.SelectedItem = value;
        SelectedCategorySelection = value is not null && _categorySelections.TryGetValue(value.Uid, out var selection) ? selection : null;
        CurrentOptions.Refresh();
        SelectedOption = CurrentOptions.Cast<ProductAttributeOptionLineModel>().FirstOrDefault();
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddGroup()
    {
        if (!CanEdit() || CommitPendingEdits?.Invoke() == false) return;
        var group = Groups.AddNew(new ProductAttributeGroupDto { Uid = Guid.NewGuid(), SelectionMode = ProductAttributeSelectionMode.Single });
        AddCategorySelection(group.Uid, []);
        SelectedGroup = group;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    private async Task DeleteGroupAsync()
    {
        if (!CanDeleteGroup() || CommitPendingEdits?.Invoke() == false || SelectedGroup is not { } group) return;
        if (!group.IsNew && !await _dialogs.ConfirmAsync("删除标签组", "删除组会同时删除其标签值和分类直接关联，已被产品使用的组由服务器拒绝，保存后生效。", "删除")) return;
        if (!CanDeleteGroup() || !ReferenceEquals(SelectedGroup, group)) return;
        using (Groups.SuppressListEvents())
        using (Options.SuppressListEvents())
        {
            foreach (var option in Options.AllLines.Where(item => item.AttributeGroupId == group.Uid).ToArray())
                if (option.IsNew) Options.RemoveCompletely(option); else Options.Remove(option);
            Groups.Remove(group);
        }
        SelectedGroup = Groups.Items.FirstOrDefault(); NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanAddOption))]
    private void AddOption()
    {
        if (!CanAddOption() || CommitPendingEdits?.Invoke() == false) return;
        SelectedOption = Options.AddNew(new ProductAttributeOptionDto { Uid = Guid.NewGuid(), AttributeGroupId = SelectedGroup!.Uid });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteOption))]
    private async Task DeleteOptionAsync()
    {
        if (!CanDeleteOption() || CommitPendingEdits?.Invoke() == false || SelectedOption is not { } option) return;
        if (!option.IsNew && !await _dialogs.ConfirmAsync("删除标签值", "已被主型号使用的标签值由服务器拒绝删除，保存后生效。", "删除")) return;
        if (!CanDeleteOption() || !ReferenceEquals(SelectedOption, option)) return;
        Options.Remove(option); SelectedOption = CurrentOptions.Cast<ProductAttributeOptionLineModel>().FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => TrySaveAsync();

    /// <summary>冻结全页请求；失败保留草稿，成功接纳回执和完整快照，缓存刷新失败单独提示。</summary>
    public async Task<bool> TrySaveAsync()
    {
        if (!CanSave() || CommitPendingEdits?.Invoke() == false) return false;
        var saved = false;
        await RunWriteAsync(async () =>
        {
            var error = await Groups.ValidateAllAsync() ?? await Options.ValidateAllAsync();
            error ??= Groups.Items.GroupBy(item => item.GroupCode.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)
                ? "全局标签组编码不能重复。" : null;
            error ??= Options.Items.GroupBy(item => (item.AttributeGroupId, item.OptionCode.Trim().ToUpperInvariant())).Any(group => group.Count() > 1)
                ? "同一标签组的标签值编码不能重复。" : null;
            error ??= _categorySelections.Where(pair => Groups.Items.Any(group => group.Uid == pair.Key)).Any(pair =>
                pair.Value.SelectedCategories.Any(candidate => !candidate.IsAvailable)
                || pair.Value.SelectedCategories.Select(candidate => candidate.Uid).Distinct().Count() != pair.Value.SelectedCategories.Count)
                ? "关联分类已失效或重复，请刷新分类候选或显式解除对应关联。" : null;
            if (error is not null) { ValidationSummary = StatusMessage = error; return; }
            if (!IsCurrentSession || !CanWrite) { ValidationSummary = StatusMessage = "当前会话或编辑权限已变化，草稿已保留。"; return; }
            var request = BuildRequest();
            var selectedUid = SelectedGroup?.Uid;
            var result = await _runner.RunAsync(() => _api.BatchSaveDefinitionsAsync(request), "保存产品标签失败");
            if (!IsCurrentSession) return;
            if (!result.Success)
            {
                ValidationSummary = StatusMessage = result.Message;
                _messages.ShowError("保存产品标签失败", result.Message); return;
            }
            var response = result.Data;
            if (response is null || !ValidReceipts(response.GroupResults, request.GroupChanges)
                || !ValidReceipts(response.OptionResults, request.OptionChanges) || !ValidReceipts(response.CategoryResults, request.CategoryChanges)
                || !ValidSnapshot(response.Definitions) || !SnapshotContainsReceipts(response))
            {
                RequiresReload = true; ValidationSummary = StatusMessage = "服务器返回成功，但回执或完整定义不可用；草稿已保留，请刷新核对结果。";
                NotifyState(); return;
            }
            var realSelectedUid = request.GroupChanges.CreateItems.Any(item => item.Data.ClientUid == selectedUid)
                ? response.GroupResults.Items.Single(item => item.ClientKey == selectedUid?.ToString("N")).Uid : selectedUid;
            LoadSnapshot(response.Definitions, realSelectedUid);
            saved = true; StatusMessage = "产品标签已保存。";
            _messages.ShowSuccess("产品标签已保存", "标签组、值和直接分类关联已一起提交。");
            try
            {
                var refresh = await _cache.RefreshProductAttributesAsync();
                if (IsCurrentSession && !refresh.Success) ReportCacheRefreshFailure(refresh.Message);
            }
            catch (Exception exception)
            {
                Trace.TraceError("产品标签保存后缓存刷新异常：{0}", exception);
                if (IsCurrentSession) ReportCacheRefreshFailure("请稍后手动刷新。");
            }
        }, "正在保存产品标签...");
        return saved;
    }

    /// <summary>保存后的共享缓存失败不回滚已经确认的私有基线。</summary>
    private void ReportCacheRefreshFailure(string message)
    {
        StatusMessage = "产品标签已保存，但缓存刷新失败；可稍后重新刷新。";
        _messages.ShowWarning("产品标签已保存，但缓存刷新失败", message);
    }

    /// <summary>构造直接关联差异；组删除的关系由后端级联，选中子分类不表示继承复制。</summary>
    private BatchSaveRequestBase<ProductCategoryAttributeGroupWriteDto> BuildCategoryChanges()
    {
        var changes = new BatchSaveRequestBase<ProductCategoryAttributeGroupWriteDto>();
        foreach (var group in Groups.Items)
        {
            var selectedIds = _categorySelections[group.Uid].SelectedCategories.Select(item => item.Uid).ToHashSet();
            var baseline = _baseline.CategoryRelations.Where(item => item.AttributeGroupId == group.Uid).ToArray();
            foreach (var categoryId in selectedIds.Except(baseline.Select(item => item.CategoryId)).OrderBy(id => id))
                changes.CreateItems.Add(new()
                {
                    ClientKey = group.Uid.ToString("N") + "/" + categoryId.ToString("N"),
                    Data = new() { AttributeGroupId = group.Uid, CategoryId = categoryId }
                });
            foreach (var relation in baseline.Where(item => !selectedIds.Contains(item.CategoryId)))
                changes.DeleteItems.Add(new() { ClientKey = relation.Uid.ToString("N"), Uid = relation.Uid, Version = relation.Version });
        }
        return changes;
    }

    /// <summary>冻结整页组和值；所有既有子项或关系变更附所属组当前版本，每组聚合只更新一次。</summary>
    public SaveProductAttributeDefinitionsRequest BuildRequest()
    {
        var deletedGroups = Groups.DeletedItems.Select(item => item.Uid).ToHashSet();
        var options = new BatchSaveRequestBase<ProductAttributeOptionWriteDto>
        {
            CreateItems = Options.AddedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId)).Select(item => new CreateItemRequest<ProductAttributeOptionWriteDto>
            { ClientKey = item.Uid.ToString("N"), Data = item.Adapt<ProductAttributeOptionWriteDto>() }).ToList(),
            UpdateItems = Options.ModifiedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId)).Select(item => new UpdateItemRequest<ProductAttributeOptionWriteDto>
            { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version, Data = item.Adapt<ProductAttributeOptionWriteDto>() }).ToList(),
            DeleteItems = Options.DeletedItems.Where(item => !deletedGroups.Contains(item.AttributeGroupId)).Select(item => new DeleteItemRequest
            { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version }).ToList()
        };
        var categories = BuildCategoryChanges();
        var touchedGroups = Options.AllLines.Where(item => item.IsDirty && !deletedGroups.Contains(item.AttributeGroupId)).Select(item => item.AttributeGroupId)
            .Concat(categories.CreateItems.Select(item => item.Data.AttributeGroupId))
            .Concat(_baseline.CategoryRelations.Where(relation => categories.DeleteItems.Any(item => item.Uid == relation.Uid)).Select(item => item.AttributeGroupId))
            .Concat(Groups.ModifiedItems.Select(item => item.Uid)).ToHashSet();
        return new()
        {
            GroupChanges = new()
            {
                CreateItems = Groups.AddedItems.Select(item => new CreateItemRequest<ProductAttributeGroupWriteDto>
                { ClientKey = item.Uid.ToString("N"), Data = item.Adapt<ProductAttributeGroupWriteDto>() }).ToList(),
                UpdateItems = Groups.Items.Where(item => !item.IsNew && touchedGroups.Contains(item.Uid)).Select(item => new UpdateItemRequest<ProductAttributeGroupWriteDto>
                { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version, Data = item.Adapt<ProductAttributeGroupWriteDto>() }).ToList(),
                DeleteItems = Groups.DeletedItems.Select(item => new DeleteItemRequest
                { ClientKey = item.Uid.ToString("N"), Uid = item.Uid, Version = item.Version }).ToList()
            },
            OptionChanges = options,
            CategoryChanges = categories
        };
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh() || !await ResolveUnsavedChangesAsync()) return;
        await LoadAsync(true);
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject() => RejectChangesCore();

    /// <summary>撤销整个私有工作区，删除池与所有组的分类草稿共同恢复，不覆盖共享缓存。</summary>
    private void RejectChangesCore()
    {
        var requiresReload = RequiresReload;
        var selectedId = SelectedGroup?.Uid;
        Groups.RejectChanges(); Options.RejectChanges();
        LoadSnapshot(_baseline, selectedId); RequiresReload = requiresReload;
        StatusMessage = "产品标签编辑已撤销。"; NotifyState();
    }

    /// <summary>页面读取检查取消、代际、会话和存活；失败保留旧完整基线。</summary>
    private async Task LoadAsync(bool refreshCache)
    {
        if (!IsCurrentSession || IsBusy || IsLoading) return;
        CancelLoad();
        var cts = _loadCts = new CancellationTokenSource(); var generation = ++_loadGeneration;
        IsLoading = true; NotifyState();
        try
        {
            var cache = refreshCache ? await _cache.RefreshProductAttributesAsync() : await _cache.EnsureProductAttributesLoadedAsync();
            if (!IsCurrentSession || generation != _loadGeneration || cts.IsCancellationRequested) return;
            if (!cache.Success) { ValidationSummary = StatusMessage = "分类及标签候选加载失败：" + cache.Message; return; }
            var result = await _runner.RunAsync(() => _api.GetDefinitionsAsync(cts.Token), cts.Token, "加载产品标签失败");
            if (!IsCurrentSession || generation != _loadGeneration || cts.IsCancellationRequested) return;
            if (!result.Success || !ValidSnapshot(result.Data))
            { ValidationSummary = StatusMessage = result.Success ? "服务器未返回完整共享标签定义。" : result.Message; return; }
            LoadSnapshot(result.Data, SelectedGroup?.Uid);
            StatusMessage = Groups.Items.Count == 0 ? "尚无标签定义，可新增标签组并选择直接适用分类。" : "产品标签已加载。";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("读取产品标签工作区异常：{0}", exception);
            if (IsCurrentSession && generation == _loadGeneration && !cts.IsCancellationRequested)
                ValidationSummary = StatusMessage = "加载产品标签失败，请稍后重试。";
        }
        finally
        {
            if (generation == _loadGeneration) { IsLoading = false; NotifyState(); }
            if (ReferenceEquals(_loadCts, cts)) { _loadCts = null; cts.Dispose(); }
        }
    }

    /// <summary>校验全局完整快照身份、版本、选项和关联归属，空快照仍正常成功。</summary>
    private static bool ValidSnapshot(ProductAttributeDefinitionsDto? definitions) =>
        definitions is not null && definitions.CategoryId == Guid.Empty && definitions.Groups is not null && definitions.CategoryRelations is not null
        && definitions.Groups.All(group => group is not null) && definitions.CategoryRelations.All(relation => relation is not null)
        && definitions.Groups.Select(group => group.Uid).Distinct().Count() == definitions.Groups.Count
        && definitions.Groups.All(group => group.Uid != Guid.Empty && group.Version > 0 && group.Options is not null
            && group.Options.All(option => option is not null && option.Uid != Guid.Empty && option.AttributeGroupId == group.Uid && option.Version > 0))
        && definitions.Groups.SelectMany(group => group.Options).Select(option => option.Uid).Distinct().Count() == definitions.Groups.Sum(group => group.Options.Count)
        && definitions.CategoryRelations.Select(relation => relation.Uid).Distinct().Count() == definitions.CategoryRelations.Count
        && definitions.CategoryRelations.All(relation => relation.Uid != Guid.Empty && relation.Version > 0 && relation.CategoryId != Guid.Empty
            && definitions.Groups.Any(group => group.Uid == relation.AttributeGroupId))
        && definitions.CategoryRelations.Select(relation => (relation.CategoryId, relation.AttributeGroupId)).Distinct().Count() == definitions.CategoryRelations.Count;

    /// <summary>核对客户端键、操作、真实身份和版本；既有软删除回执允许版本保持不变。</summary>
    private static bool ValidReceipts<T>(BatchSaveResponseBase? result, BatchSaveRequestBase<T> request)
    {
        if (result is null || result.Items is null || result.Items.Count != request.TotalCount || result.Items.Any(item => item is null) || !result.AllSucceeded
            || result.Items.Any(item => item.Uid is null || item.Uid == Guid.Empty || item.NewVersion is null or <= 0)) return false;
        foreach (var item in request.CreateItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Create) != 1) return false;
        foreach (var item in request.UpdateItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Update
                && receipt.Uid == item.Uid && receipt.OldVersion == item.Version && receipt.NewVersion == item.Version + 1) != 1) return false;
        foreach (var item in request.DeleteItems)
            if (result.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Delete
                && receipt.Uid == item.Uid && receipt.OldVersion == item.Version && receipt.NewVersion >= item.Version) != 1) return false;
        return true;
    }

    /// <summary>成功回执必须在提交后的完整快照中得到相同版本，删除回执则必须已退出有效集合。</summary>
    private static bool SnapshotContainsReceipts(SaveProductAttributeDefinitionsResponse response)
    {
        var groups = response.Definitions.Groups.ToDictionary(item => item.Uid, item => item.Version);
        var options = response.Definitions.Groups.SelectMany(item => item.Options).ToDictionary(item => item.Uid, item => item.Version);
        var relations = response.Definitions.CategoryRelations.ToDictionary(item => item.Uid, item => item.Version);
        return Matches(response.GroupResults.Items, groups) && Matches(response.OptionResults.Items, options) && Matches(response.CategoryResults.Items, relations);
        static bool Matches(IEnumerable<SaveItemResult> receipts, IReadOnlyDictionary<Guid, int> versions) => receipts.All(receipt =>
            receipt.Operation == SaveOperation.Delete ? !versions.ContainsKey(receipt.Uid!.Value) :
            versions.TryGetValue(receipt.Uid!.Value, out var version) && version == receipt.NewVersion);
    }

    /// <summary>复制服务器全局快照建立私有基线，当前组通过真实身份恢复。</summary>
    private void LoadSnapshot(ProductAttributeDefinitionsDto definitions, Guid? selectedUid)
    {
        _baseline = definitions.Adapt<ProductAttributeDefinitionsDto>();

        foreach (var selection in _categorySelections.Values)
        {
            selection.PropertyChanged -= OnCategorySelectionChanged; selection.Dispose();
        }

        _categorySelections.Clear(); _categoryCandidates.Clear();

        foreach (var category in _cache.ProductCategories)
            _categoryCandidates.Add(new(category.Uid, category.CategoryPath));

        foreach (
                var id in _baseline.CategoryRelations.
                Select(relation => relation.CategoryId).
                Distinct().
                Where(id => !_categoryCandidates.
                Any(candidate => candidate.Uid == id))
            )
            _categoryCandidates.Add(new(id, "分类未在缓存中找到：" + id, false));

        foreach (var group in _baseline.Groups)
            AddCategorySelection(group.Uid, _baseline.CategoryRelations.Where(relation => relation.AttributeGroupId == group.Uid)
                .Select(relation => _categoryCandidates.Single(candidate => candidate.Uid == relation.CategoryId)));

        Groups.LoadFromDtos(_baseline.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));

        Options.LoadFromDtos(_baseline.Groups.SelectMany(item => item.Options).OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));

        SelectedGroup = Groups.Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Groups.Items.FirstOrDefault();

        IsLoaded = true; RequiresReload = false; ValidationSummary = null; NotifyState();
    }

    /// <summary>一个组拥有独立勾选集合及其身份基线，切组不共用状态。</summary>
    private void AddCategorySelection(Guid groupId, IEnumerable<ProductAttributeCategoryCandidate> selected)
    {
        var selection = new ProductAttributeCategorySelectionModel(selected);
        _categorySelections.Add(groupId, selection); selection.PropertyChanged += OnCategorySelectionChanged;
    }

    private void OnGroupStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeGroupLineModel> args) => NotifyState();
    private void OnOptionStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeOptionLineModel> args) => NotifyState();
    private void OnCategorySelectionChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ProductAttributeCategorySelectionModel.HasChanges)) NotifyState(); }
    private void OnPermissionsChanged(object? sender, EventArgs args)
    {
        if (!_user.IsAuthenticated || _user.CurrentUser?.UserId != _sessionUserId) { _sessionEnded = true; CancelLoad(); }
        IsReadOnly = !_user.HasPermission(PermissionKeys.ProductsCategoryEdit); NotifyState();
    }
    private void OnOwnStateChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) NotifyState(); }

    /// <summary>通知按钮、字段只读及工作区关闭镜像；不把共享 DTO 当作脏跟踪对象。</summary>
    private void NotifyState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(PendingChangeSummary)); OnPropertyChanged(nameof(IsInteractionEnabled));
        OnPropertyChanged(nameof(CanEditDefinitions)); OnPropertyChanged(nameof(CanEditCurrentGroup)); OnPropertyChanged(nameof(AreGridsReadOnly)); OnPropertyChanged(nameof(EditStateMessage));
        AddGroupCommand.NotifyCanExecuteChanged(); DeleteGroupCommand.NotifyCanExecuteChanged(); AddOptionCommand.NotifyCanExecuteChanged();
        DeleteOptionCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged();
    }
    private bool CanEdit() => CanEditDefinitions;
    private bool CanAddOption() => CanEditCurrentGroup;
    private bool CanDeleteGroup() => CanEditCurrentGroup;
    private bool CanDeleteOption() => CanEditCurrentGroup && SelectedOption is not null;
    private bool CanSave() => CanEditDefinitions && HasUnsavedChanges;
    private bool CanRefresh() => IsInteractionEnabled;
    private bool CanReject() => IsInteractionEnabled && HasUnsavedChanges;

    /// <summary>未知技术错误只记录诊断，页面使用稳定反馈并保留当前编辑。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { Trace.TraceError("产品标签页面操作异常：{0}", exception); ValidationSummary = StatusMessage = "操作未完成，输入已保留，请稍后重试。"; return Task.CompletedTask; }

    private void CancelLoad()
    { _loadGeneration++; _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null; IsLoading = false; }

    /// <summary>关闭或会话结束时释放私有模型及所有订阅，晚到响应不能回写页面。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelLoad();
        Groups.ListStateChanged -= OnGroupStateChanged; Options.ListStateChanged -= OnOptionStateChanged;
        PropertyChanged -= OnOwnStateChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
        foreach (var selection in _categorySelections.Values) { selection.PropertyChanged -= OnCategorySelectionChanged; selection.Dispose(); }
        CommitPendingEdits = null;
    }
}


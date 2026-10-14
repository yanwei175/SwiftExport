using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Attributes.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
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

/// <summary>独立维护全局标签组、值和直接属性模板关系，整页草稿只在原子保存成功后接纳。</summary>
public sealed partial class ProductAttributeManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>产品属性工作区的稳定单实例路由身份。</summary>
    public const string RouteKey = "product-attributes";
    private readonly IProductAttributeApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private readonly INavigationService _navigation;
    private readonly string? _sessionUserId;
    private readonly Dictionary<Guid, ProductAttributeProfileSelectionModel> _profileSelections = [];
    private readonly ObservableCollection<ProductAttributeProfileCandidate> _profileCandidates = [];
    private ProductAttributeDefinitionsDto _baseline = new();
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private bool _disposed, _sessionEnded;
    private bool _profileRefreshPending;

    /// <summary>使用现有定义 API、缓存及反馈依赖；候选和勾选对象均复制为页面私有模型。</summary>
    public ProductAttributeManagerViewModel(IProductAttributeApi api, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs, ICurrentUserService user, INavigationService navigation)
    {
        _api = api; _runner = runner; _cache = cache; _messages = messages; _dialogs = dialogs; _user = user; _navigation = navigation;
        _sessionUserId = user.CurrentUser?.UserId;
        Title = "自定义属性";
        IsReadOnly = !user.HasPermission(PermissionKeys.ProductsCategoryEdit);
        Groups = new(dto => new ProductAttributeGroupLineModel());
        Options = new(dto => new ProductAttributeOptionLineModel());
        ProfileCandidates = new(_profileCandidates);
        CurrentOptions = CollectionViewSource.GetDefaultView(Options.Items);
        CurrentOptions.Filter = item => item is ProductAttributeOptionLineModel option && option.AttributeGroupId == SelectedGroup?.Uid;
        Groups.ListStateChanged += OnGroupStateChanged; Options.ListStateChanged += OnOptionStateChanged;
        PropertyChanged += OnOwnStateChanged; _user.CurrentUserChanged += OnPermissionsChanged;
        _cache.ProductAttributesChanged += OnProfileCacheChanged;
        foreach (var command in new IRelayCommand[] { AddGroupCommand, DeleteGroupCommand, AddOptionCommand,
            DeleteOptionCommand, SaveCommand, RefreshCommand, RejectCommand, ManageAttributeProfilesCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>全局共享组的私有编辑状态及删除池。</summary>
    public BaseListModel<ProductAttributeGroupLineModel, ProductAttributeGroupDto> Groups { get; }
    /// <summary>全局选项工作区；切组只切显示范围，不替换草稿。</summary>
    public BaseListModel<ProductAttributeOptionLineModel, ProductAttributeOptionDto> Options { get; }
    /// <summary>仅展示选中组的选项，保留其余组选项编辑。</summary>
    public ICollectionView CurrentOptions { get; }
    /// <summary>完整路径属性模板候选；缺失的既有关联仍保留可见占位，不能静默丢弃。</summary>
    public ReadOnlyObservableCollection<ProductAttributeProfileCandidate> ProfileCandidates { get; }
    /// <summary>单选、多选标签规则候选。</summary>
    public IReadOnlyList<KeyValuePair<ProductAttributeSelectionMode, string>> SelectionModes { get; } =
        [new(ProductAttributeSelectionMode.Single, "单选"), new(ProductAttributeSelectionMode.Multiple, "多选")];
    /// <summary>视觉层提交原生单元格编辑并关闭未确认弹出层的窄回调。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>视觉层告知多选弹出层是否打开；刷新候选不能覆盖尚未确认的勾选。</summary>
    public Func<bool>? IsProfilePopupOpen { get; set; }
    /// <summary>模板维护入口复用字典读取权限，编辑仍由字典明细分别授权。</summary>
    public bool CanManageAttributeProfiles => IsInteractionEnabled && _user.HasPermission(PermissionKeys.SystemDictionaryView);
    /// <summary>工作区单实例标题由导航注册路由负责去重。</summary>
    public string DocumentTitle => Title;

    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool IsResolvingChanges { get; private set; }
    [ObservableProperty] public partial bool RequiresReload { get; private set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "尚未读取产品属性。";
    [ObservableProperty] public partial ProductAttributeGroupLineModel? SelectedGroup { get; set; }
    [ObservableProperty] public partial ProductAttributeProfileSelectionModel? SelectedProfileSelection { get; private set; }
    [ObservableProperty] public partial int SelectedTabIndex { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteOptionCommand))]
    public partial ProductAttributeOptionLineModel? SelectedOption { get; set; }

    /// <summary>真实草稿包括组和值删除池，以及仍在工作区中的直接关系差异。</summary>
    public bool HasUnsavedChanges => Groups.HasChanges || Options.HasChanges || Groups.Items.Any(group => _profileSelections.TryGetValue(group.Uid, out var selection) && selection.HasChanges);
    /// <summary>显示整页待保存影响，删除行不再可见时仍明确提示。</summary>
    public string PendingChangeSummary => HasUnsavedChanges
        ? $"待保存：新增组 {Groups.AddedItems.Count}、修改组 {Groups.ModifiedItems.Count}、删除组 {Groups.DeletedItems.Count}；新增值 {Options.AddedItems.Count}、修改值 {Options.ModifiedItems.Count}、删除值 {Options.DeletedItems.Count}；属性模板关联修改 {_profileSelections.Count(pair => Groups.Items.Any(group => group.Uid == pair.Key) && pair.Value.HasChanges)} 组。"
        : string.Empty;
    /// <summary>当前会话有效且无读写或离开确认时才能交互。</summary>
    public bool IsInteractionEnabled => IsCurrentSession && IsNotBusy && !IsLoading && !IsResolvingChanges;
    /// <summary>写权限、完整服务器基线及运行状态共同决定字段编辑能力。</summary>
    public bool CanEditDefinitions => IsInteractionEnabled && IsLoaded && !RequiresReload && CanWrite;
    /// <summary>组选中后才允许操作对应的选项与属性模板关系。</summary>
    public bool CanEditCurrentGroup => CanEditDefinitions && SelectedGroup is not null;
    /// <summary>DataGrid 保持只读展示，实际写入由页面和后端权限共同保护。</summary>
    public bool AreGridsReadOnly => !CanEditDefinitions;
    /// <summary>说明会话、权限及回执不明导致的编辑限制。</summary>
    public string EditStateMessage => !IsCurrentSession ? "登录会话已结束，请重新打开页面。" :
        !CanWrite ? "当前会话无权编辑产品属性。" :
        RequiresReload ? "保存结果需要核对，请刷新服务器数据后继续。" :
        IsLoading ? "正在读取属性定义和属性模板候选。" :
        !IsLoaded ? "尚未成功读取完整属性定义。" : string.Empty;
    private bool IsCurrentSession => !_disposed && !_sessionEnded && _user.IsAuthenticated && _user.CurrentUser?.UserId == _sessionUserId;

    /// <summary>提交当前原生编辑后激活模板管理页签；不保存或替换标签定义草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenAttributeProfiles))]
    private async Task ManageAttributeProfilesAsync()
    {
        if (!CanOpenAttributeProfiles() || CommitPendingEdits?.Invoke() == false) return;
        var request = new DictionaryDataDocumentOpenRequest(DictCodes.ProductAttributeProfile, "自定义属性模板",
            "模板明细与字典管理共用；已被产品或属性组使用的模板不能删除、停用。");
        await _navigation.OpenDocumentAsync(DictionaryDataManagerViewModel.RouteKey, request.DocumentKey, request);
    }

    private bool CanOpenAttributeProfiles() => CanManageAttributeProfiles;

    /// <summary>弹出层关闭后接纳延迟缓存更新，只替换候选显示，不重载组、值或关系草稿。</summary>
    public void ApplyPendingProfileRefresh()
    {
        if (!_profileRefreshPending || !IsCurrentSession || IsProfilePopupOpen?.Invoke() == true) return;
        _profileRefreshPending = false;
        RefreshProfileCandidates();
    }

    /// <summary>缓存更新在多选弹层关闭后才接纳，避免覆盖尚未确认的临时勾选。</summary>
    private void OnProfileCacheChanged(object? sender, EventArgs args)
    {
        if (!IsCurrentSession || !IsLoaded) return;
        if (IsProfilePopupOpen?.Invoke() == true) { _profileRefreshPending = true; return; }
        RefreshProfileCandidates();
    }

    /// <summary>按 Uid 重新匹配候选和每组已确认选择，保留原关系身份基线与缺失项。</summary>
    private void RefreshProfileCandidates()
    {
        var candidates = _cache.GetDictionaryValues(SwiftExport.Contracts.SysEnums.DictCodes.ProductAttributeProfile)
            .Select(profile => new ProductAttributeProfileCandidate(profile.Uid, profile.DataNameZh ?? profile.DataNameEn ?? string.Empty,
                profile.DataNameEn ?? string.Empty)).ToDictionary(item => item.Uid);
        var selectedIds = _profileSelections.Values.SelectMany(selection => selection.SelectedProfiles.Select(profile => profile.Uid))
            .Concat(_baseline.ProfileRelations.Select(relation => relation.ProductAttributeProfileId)).Distinct();
        foreach (var id in selectedIds)
            if (!candidates.ContainsKey(id)) candidates.Add(id, new(id, "属性模板未在缓存中找到：" + id, isAvailable: false));
        _profileCandidates.Clear();
        foreach (var candidate in candidates.Values) _profileCandidates.Add(candidate);
        foreach (var selection in _profileSelections.Values)
        {
            var ids = selection.SelectedProfiles.Select(item => item.Uid).ToArray();
            selection.SelectedProfiles.Clear();
            foreach (var id in ids) selection.SelectedProfiles.Add(candidates[id]);
        }
        NotifyState();
    }

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
            decision = await _dialogs.ShowAsync("产品属性有未保存修改，保存将提交整页的属性组、值和属性模板关联。",
                new DialogOptions("产品属性未保存") { PrimaryButtonText = "保存", SecondaryButtonText = "放弃", CloseButtonText = "取消" },
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
        SelectedProfileSelection = value is not null && _profileSelections.TryGetValue(value.Uid, out var selection) ? selection : null;
        CurrentOptions.Refresh();
        SelectedOption = CurrentOptions.Cast<ProductAttributeOptionLineModel>().FirstOrDefault();
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddGroup()
    {
        if (!CanEdit() || CommitPendingEdits?.Invoke() == false) return;
        var group = Groups.AddNew(new ProductAttributeGroupDto { Uid = Guid.NewGuid(), SelectionMode = ProductAttributeSelectionMode.Single });
        AddProfileSelection(group.Uid, []);
        SelectedGroup = group;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    private async Task DeleteGroupAsync()
    {
        if (!CanDeleteGroup() || CommitPendingEdits?.Invoke() == false || SelectedGroup is not { } group) return;
        if (!group.IsNew && !await _dialogs.ConfirmAsync("删除标签组", "删除组会同时删除其标签值和属性模板直接关联，已被产品使用的组由服务器拒绝，保存后生效。", "删除")) return;
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
        if (!option.IsNew && !await _dialogs.ConfirmAsync("删除标签值", "已被产品对象使用的标签值由服务器拒绝删除，保存后生效。", "删除")) return;
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
            error ??= _profileSelections.Where(pair => Groups.Items.Any(group => group.Uid == pair.Key)).Any(pair =>
                pair.Value.SelectedProfiles.Any(candidate => !candidate.IsAvailable)
                || pair.Value.SelectedProfiles.Select(candidate => candidate.Uid).Distinct().Count() != pair.Value.SelectedProfiles.Count)
                ? "关联属性模板已失效或重复，请刷新属性模板候选或显式解除对应关联。" : null;
            if (error is not null) { ValidationSummary = StatusMessage = error; return; }
            if (!IsCurrentSession || !CanWrite) { ValidationSummary = StatusMessage = "当前会话或编辑权限已变化，草稿已保留。"; return; }
            var request = BuildRequest();
            var selectedUid = SelectedGroup?.Uid;
            var result = await _runner.RunAsync(() => _api.BatchSaveDefinitionsAsync(request), "保存产品属性失败");
            if (!IsCurrentSession) return;
            if (!result.Success)
            {
                ValidationSummary = StatusMessage = result.Message;
                _messages.ShowError("保存产品属性失败", result.Message); return;
            }
            var response = result.Data;
            if (response is null || !ValidReceipts(response.GroupResults, request.GroupChanges)
                || !ValidReceipts(response.OptionResults, request.OptionChanges) || !ValidReceipts(response.ProfileResults, request.ProfileChanges)
                || !ValidSnapshot(response.Definitions) || !SnapshotContainsReceipts(response))
            {
                RequiresReload = true; ValidationSummary = StatusMessage = "服务器返回成功，但回执或完整定义不可用；草稿已保留，请刷新核对结果。";
                NotifyState(); return;
            }
            var realSelectedUid = request.GroupChanges.CreateItems.Any(item => item.Data.ClientUid == selectedUid)
                ? response.GroupResults.Items.Single(item => item.ClientKey == selectedUid?.ToString("N")).Uid : selectedUid;
            LoadSnapshot(response.Definitions, realSelectedUid);
            saved = true; StatusMessage = "产品属性已保存。";
            _messages.ShowSuccess("产品属性已保存", "属性组、值和直接属性模板关联已一起提交。");
            try
            {
                var refresh = await _cache.RefreshProductAttributesAsync();
                if (IsCurrentSession && !refresh.Success) ReportCacheRefreshFailure(refresh.Message);
            }
            catch (Exception exception)
            {
                Trace.TraceError("产品属性保存后缓存刷新异常：{0}", exception);
                if (IsCurrentSession) ReportCacheRefreshFailure("请稍后手动刷新。");
            }
        }, "正在保存产品属性...");
        return saved;
    }

    /// <summary>保存后的共享缓存失败不回滚已经确认的私有基线。</summary>
    private void ReportCacheRefreshFailure(string message)
    {
        StatusMessage = "产品属性已保存，但缓存刷新失败；可稍后重新刷新。";
        _messages.ShowWarning("产品属性已保存，但缓存刷新失败", message);
    }

    /// <summary>构造直接关联差异；组删除的关系由后端级联，模板只保存直接关联。</summary>
    private BatchSaveRequestBase<ProductAttributeProfileGroupWriteDto> BuildProfileChanges()
    {
        var changes = new BatchSaveRequestBase<ProductAttributeProfileGroupWriteDto>();
        foreach (var group in Groups.Items)
        {
            var selectedIds = _profileSelections[group.Uid].SelectedProfiles.Select(item => item.Uid).ToHashSet();
            var baseline = _baseline.ProfileRelations.Where(item => item.AttributeGroupId == group.Uid).ToArray();
            foreach (var profileId in selectedIds.Except(baseline.Select(item => item.ProductAttributeProfileId)).OrderBy(id => id))
                changes.CreateItems.Add(new()
                {
                    ClientKey = group.Uid.ToString("N") + "/" + profileId.ToString("N"),
                    Data = new() { AttributeGroupId = group.Uid, ProductAttributeProfileId = profileId }
                });
            foreach (var relation in baseline.Where(item => !selectedIds.Contains(item.ProductAttributeProfileId)))
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
        var profiles = BuildProfileChanges();
        var touchedGroups = Options.AllLines.Where(item => item.IsDirty && !deletedGroups.Contains(item.AttributeGroupId)).Select(item => item.AttributeGroupId)
            .Concat(profiles.CreateItems.Select(item => item.Data.AttributeGroupId))
            .Concat(_baseline.ProfileRelations.Where(relation => profiles.DeleteItems.Any(item => item.Uid == relation.Uid)).Select(item => item.AttributeGroupId))
            .Concat(Groups.ModifiedItems.Select(item => item.Uid)).ToHashSet();
        return new()
        {
            ContractVersion = 2,
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
            ProfileChanges = profiles
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

    /// <summary>撤销整个私有工作区，删除池与所有组的属性模板草稿共同恢复，不覆盖共享缓存。</summary>
    private void RejectChangesCore()
    {
        var requiresReload = RequiresReload;
        var selectedId = SelectedGroup?.Uid;
        Groups.RejectChanges(); Options.RejectChanges();
        LoadSnapshot(_baseline, selectedId); RequiresReload = requiresReload;
        StatusMessage = "产品属性编辑已撤销。"; NotifyState();
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
            if (!cache.Success) { ValidationSummary = StatusMessage = "属性模板及属性候选加载失败：" + cache.Message; return; }
            var result = await _runner.RunAsync(() => _api.GetDefinitionsAsync(cts.Token), cts.Token, "加载产品属性失败");
            if (!IsCurrentSession || generation != _loadGeneration || cts.IsCancellationRequested) return;
            if (!result.Success || !ValidSnapshot(result.Data))
            { ValidationSummary = StatusMessage = result.Success ? "服务器未返回完整共享标属性定义。" : result.Message; return; }
            LoadSnapshot(result.Data, SelectedGroup?.Uid);
            StatusMessage = Groups.Items.Count == 0 ? "尚无属性定义，可新增属性组并选择直接适用属性模板。" : "产品属性已加载。";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("读取产品属性工作区异常：{0}", exception);
            if (IsCurrentSession && generation == _loadGeneration && !cts.IsCancellationRequested)
                ValidationSummary = StatusMessage = "加载产品属性失败，请稍后重试。";
        }
        finally
        {
            if (generation == _loadGeneration) { IsLoading = false; NotifyState(); }
            if (ReferenceEquals(_loadCts, cts)) { _loadCts = null; cts.Dispose(); }
        }
    }

    /// <summary>校验全局完整快照身份、版本、选项和关联归属，空快照仍正常成功。</summary>
    private static bool ValidSnapshot(ProductAttributeDefinitionsDto? definitions) =>
        definitions is not null && definitions.ProfileId == Guid.Empty && definitions.Groups is not null && definitions.ProfileRelations is not null
        && definitions.Groups.All(group => group is not null) && definitions.ProfileRelations.All(relation => relation is not null)
        && definitions.Groups.Select(group => group.Uid).Distinct().Count() == definitions.Groups.Count
        && definitions.Groups.All(group => group.Uid != Guid.Empty && group.Version > 0 && group.Options is not null
            && group.Options.All(option => option is not null && option.Uid != Guid.Empty && option.AttributeGroupId == group.Uid && option.Version > 0))
        && definitions.Groups.SelectMany(group => group.Options).Select(option => option.Uid).Distinct().Count() == definitions.Groups.Sum(group => group.Options.Count)
        && definitions.ProfileRelations.Select(relation => relation.Uid).Distinct().Count() == definitions.ProfileRelations.Count
        && definitions.ProfileRelations.All(relation => relation.Uid != Guid.Empty && relation.Version > 0 && relation.ProductAttributeProfileId != Guid.Empty
            && definitions.Groups.Any(group => group.Uid == relation.AttributeGroupId))
        && definitions.ProfileRelations.Select(relation => (relation.ProductAttributeProfileId, relation.AttributeGroupId)).Distinct().Count() == definitions.ProfileRelations.Count;

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
        var relations = response.Definitions.ProfileRelations.ToDictionary(item => item.Uid, item => item.Version);
        return Matches(response.GroupResults.Items, groups) && Matches(response.OptionResults.Items, options) && Matches(response.ProfileResults.Items, relations);
        static bool Matches(IEnumerable<SaveItemResult> receipts, IReadOnlyDictionary<Guid, int> versions) => receipts.All(receipt =>
            receipt.Operation == SaveOperation.Delete ? !versions.ContainsKey(receipt.Uid!.Value) :
            versions.TryGetValue(receipt.Uid!.Value, out var version) && version == receipt.NewVersion);
    }

    /// <summary>复制服务器全局快照建立私有基线，当前组通过真实身份恢复。</summary>
    private void LoadSnapshot(ProductAttributeDefinitionsDto definitions, Guid? selectedUid)
    {
        _baseline = definitions.Adapt<ProductAttributeDefinitionsDto>();

        foreach (var selection in _profileSelections.Values)
        {
            selection.PropertyChanged -= OnProfileSelectionChanged; selection.Dispose();
        }

        _profileSelections.Clear(); _profileCandidates.Clear();

        foreach (var profile in _cache.GetDictionaryValues(SwiftExport.Contracts.SysEnums.DictCodes.ProductAttributeProfile))
            _profileCandidates.Add(new(profile.Uid, profile.DataNameZh ?? profile.DataNameEn ?? string.Empty, profile.DataNameEn ?? string.Empty));

        foreach (
                var id in _baseline.ProfileRelations.
                Select(relation => relation.ProductAttributeProfileId).
                Distinct().
                Where(id => !_profileCandidates.
                Any(candidate => candidate.Uid == id))
            )
            _profileCandidates.Add(new(id, "属性模板未在缓存中找到：" + id, isAvailable: false));

        foreach (var group in _baseline.Groups)
            AddProfileSelection(group.Uid, _baseline.ProfileRelations.Where(relation => relation.AttributeGroupId == group.Uid)
                .Select(relation => _profileCandidates.Single(candidate => candidate.Uid == relation.ProductAttributeProfileId)));

        Groups.LoadFromDtos(_baseline.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));

        Options.LoadFromDtos(_baseline.Groups.SelectMany(item => item.Options).OrderBy(item => item.SortOrder).ThenBy(item => item.Uid));

        SelectedGroup = Groups.Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Groups.Items.FirstOrDefault();

        IsLoaded = true; RequiresReload = false; ValidationSummary = null; NotifyState();
    }

    /// <summary>一个组拥有独立勾选集合及其身份基线，切组不共用状态。</summary>
    private void AddProfileSelection(Guid groupId, IEnumerable<ProductAttributeProfileCandidate> selected)
    {
        var selection = new ProductAttributeProfileSelectionModel(selected);
        _profileSelections.Add(groupId, selection); selection.PropertyChanged += OnProfileSelectionChanged;
    }

    private void OnGroupStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeGroupLineModel> args) => NotifyState();
    private void OnOptionStateChanged(object? sender, ListStateChangedEventArgs<ProductAttributeOptionLineModel> args) => NotifyState();
    private void OnProfileSelectionChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ProductAttributeProfileSelectionModel.HasChanges)) NotifyState(); }
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
        OnPropertyChanged(nameof(CanManageAttributeProfiles)); ManageAttributeProfilesCommand.NotifyCanExecuteChanged();
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
    { Trace.TraceError("产品属性页面操作异常：{0}", exception); ValidationSummary = StatusMessage = "操作未完成，输入已保留，请稍后重试。"; return Task.CompletedTask; }

    private void CancelLoad()
    { _loadGeneration++; _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null; IsLoading = false; }

    /// <summary>关闭或会话结束时释放私有模型及所有订阅，晚到响应不能回写页面。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelLoad();
        Groups.ListStateChanged -= OnGroupStateChanged; Options.ListStateChanged -= OnOptionStateChanged;
        PropertyChanged -= OnOwnStateChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
        _cache.ProductAttributesChanged -= OnProfileCacheChanged;
        foreach (var selection in _profileSelections.Values) { selection.PropertyChanged -= OnProfileSelectionChanged; selection.Dispose(); }
        CommitPendingEdits = null;
        IsProfilePopupOpen = null;
    }
}


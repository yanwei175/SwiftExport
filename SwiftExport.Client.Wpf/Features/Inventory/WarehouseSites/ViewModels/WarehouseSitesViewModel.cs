using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.WarehouseSites.Models;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

/// <summary>编排供应商虚拟仓只读列表、分页查询、右侧新增/改名抽屉及受权停用和删除命令。</summary>
public sealed partial class WarehouseSitesViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    private enum ListLoadResult
    {
        Ignored,
        Failed,
        Succeeded
    }

    /// <summary>供应商虚拟仓页面的稳定导航键。</summary>
    public const string RouteKey = "inventory-warehouse-sites";
    private const int DefaultPageSize = 20;
    private readonly IWarehouseSiteApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IWarehouseSiteReasonDialogService _reasonDialog;
    private readonly ICurrentUserService _currentUser;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly Dictionary<string, Guid> _writeIntents = new(StringComparer.Ordinal);
    private CancellationTokenSource? _listCancellation;
    private CancellationTokenSource? _detailCancellation;
    private int _listRequestVersion;
    private int _detailRequestVersion;
    private bool _initialized;
    private bool _isAlive = true;

    /// <summary>创建页面编排器并接入当前会话权限快照。</summary>
    public WarehouseSitesViewModel(
        IWarehouseSiteApi api,
        IApiCallRunner runner,
        IMessageService messages,
        IDialogService dialogs,
        IWarehouseSiteReasonDialogService reasonDialog,
        ICurrentUserService currentUser,
        IReferenceDataCacheService referenceDataCache)
    {
        _api = api;
        _runner = runner;
        _messages = messages;
        _dialogs = dialogs;
        _reasonDialog = reasonDialog;
        _currentUser = currentUser;
        _referenceDataCache = referenceDataCache;
        Title = "供应商虚拟仓";
        StatusOptions =
        [
            new WarehouseSiteStatusFilter("全部状态", null),
            new WarehouseSiteStatusFilter("活动", true),
            new WarehouseSiteStatusFilter("停用", false)
        ];
        SelectedStatus = StatusOptions[0];
        ApplyPermissions();
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        RegisterBusyAwareCommand(OpenCreateCommand);
        RegisterBusyAwareCommand(OpenEditCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(CloseDrawerCommand);
        RegisterBusyAwareCommand(DeactivateCommand);
        RegisterBusyAwareCommand(DeleteCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<WarehouseSiteDto> Items { get; } = [];
    /// <summary>获取当前登录会话可见的供应商缓存，供筛选与新建抽屉本地精确选择。</summary>
    public IEnumerable<SupplierDto> SupplierOptions => _referenceDataCache.Suppliers;
    public IReadOnlyList<WarehouseSiteStatusFilter> StatusOptions { get; }
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
    public string PageSummary => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierDto? SearchSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSiteStatusFilter SelectedStatus { get; set; }
    [ObservableProperty] public partial string? ResultSummary { get; set; } = "尚未加载。";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int PageIndex { get; set; } = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int PageSize { get; set; } = DefaultPageSize;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int TotalCount { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial WarehouseSiteDto? SelectedItem { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseDrawerCommand))]
    public partial bool IsDrawerOpen { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial WarehouseSiteEditModel? EditModel { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool HasManagePermission { get; private set; }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_initialized || !_isAlive) return;
        _initialized = true;
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        if (!_isAlive) return;
        OnPropertyChanged(nameof(SupplierOptions));
        if (!cacheResult.Success)
            _messages.ShowError("供应商缓存未更新", cacheResult.Message);
        await LoadWithBusyAsync(null, "正在查询虚拟仓...");
    }

    partial void OnEditModelChanging(WarehouseSiteEditModel? value)
    {
        if (EditModel is not null) EditModel.PropertyChanged -= OnEditModelPropertyChanged;
    }

    partial void OnEditModelChanged(WarehouseSiteEditModel? value)
    {
        if (value is not null) value.PropertyChanged += OnEditModelPropertyChanged;
        UpdateEditState();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!await PrepareDrawerReplacementAsync()) return;
        PageIndex = 1;
        await LoadWithBusyAsync(null, "正在查询虚拟仓...");
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!await PrepareDrawerReplacementAsync()) return;
        await LoadWithBusyAsync(SelectedItem?.Uid, "正在刷新虚拟仓...");
    }

    [RelayCommand]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await PrepareDrawerReplacementAsync()) return;
        var oldIndex = PageIndex;
        var oldSize = PageSize;
        PageIndex = Math.Max(1, request.NewIndex);
        PageSize = request.NewSize <= 0 ? DefaultPageSize : request.NewSize;
        if (!await LoadWithBusyAsync(null, "正在切换分页..."))
        {
            PageIndex = oldIndex;
            PageSize = oldSize;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenCreate))]
    private async Task OpenCreateAsync()
    {
        if (!await PrepareDrawerReplacementAsync()) return;
        EditModel = WarehouseSiteEditModel.CreateNew();
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanOpenEdit))]
    private async Task OpenEditAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await PrepareDrawerReplacementAsync()) return;
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        var requestVersion = ++_detailRequestVersion;
        ServiceResult<WarehouseSiteDto>? result = null;
        await RunBusyAsync(async () =>
        {
            result = await _runner.RunAsync(
                () => _api.GetAsync(selected.Uid, cancellation.Token), cancellation.Token, "加载虚拟仓详情失败");
        }, "正在加载虚拟仓详情...");
        if (!_isAlive || cancellation.IsCancellationRequested
            || !ReferenceEquals(_detailCancellation, cancellation) || _detailRequestVersion != requestVersion)
        {
            cancellation.Dispose();
            return;
        }
        _detailCancellation = null;
        cancellation.Dispose();
        if (result is null || !result.Success || result.Data is null)
        {
            _messages.ShowError("无法编辑虚拟仓", result?.Message ?? "虚拟仓详情加载失败。");
            return;
        }
        EditModel = WarehouseSiteEditModel.FromDto(result.Data);
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanCloseDrawer))]
    private async Task CloseDrawerAsync() => await PrepareDrawerReplacementAsync();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var editor = EditModel;
        if (editor is null || !editor.ValidateForSave())
        {
            _messages.ShowWarning("资料未保存", "请检查供应商、仓库代码和仓库名称。");
            return;
        }
        await RunWriteAsync(async () =>
        {
            ServiceResult<WarehouseSiteDto> result;
            if (editor.IsCreateMode)
            {
                result = await _runner.RunAsync(() => _api.CreateSupplierVirtualAsync(new CreateSupplierVirtualWarehouseCommand
                {
                    CommandId = editor.GetOrCreateCommandId(), SupplierId = editor.SelectedSupplier!.Uid,
                    Code = editor.Code.Trim(), Name = editor.Name.Trim()
                }), "创建虚拟仓失败");
            }
            else
            {
                result = await _runner.RunAsync(() => _api.UpdateAsync(editor.Uid, new UpdateWarehouseSiteCommand
                {
                    CommandId = editor.GetOrCreateCommandId(), ExpectedVersion = editor.Version, Name = editor.Name.Trim()
                }), "保存虚拟仓失败");
            }
            if (!result.Success || result.Data is null)
            {
                _messages.ShowError("虚拟仓未保存", result.Message);
                return;
            }
            var savedId = result.Data.Uid;
            var action = editor.IsCreateMode ? "已创建虚拟仓" : "已保存虚拟仓";
            editor.Accept(result.Data);
            CloseDrawerCore();
            var refreshed = await LoadCoreAsync(savedId) == ListLoadResult.Succeeded;
            _messages.ShowSuccess(action, result.Data.Code);
            if (!refreshed) _messages.ShowWarning(action + "，但列表刷新失败", ResultSummary ?? "请稍后刷新列表。");
            await RefreshWarehousePickerCacheAfterWriteAsync("保存成功，缓存刷新失败");
        }, editor.IsCreateMode ? "正在创建虚拟仓..." : "正在保存虚拟仓...");
    }

    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private async Task DeactivateAsync()
    {
        var target = SelectedItem;
        if (target is null) return;
        var reason = await _reasonDialog.RequestAsync("停用", target);
        if (reason is null) return;
        if (SelectedItem?.Uid != target.Uid || SelectedItem.Version != target.Version)
        {
            _messages.ShowWarning("未执行停用", "确认期间所选虚拟仓已经变化，请重新选择。");
            return;
        }
        var intentKey = CreateIntentKey("deactivate", target, reason);
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.DeactivateAsync(target.Uid, new DeactivateWarehouseSiteCommand
            {
                CommandId = GetOrCreateIntent(intentKey), ExpectedVersion = target.Version, Reason = reason
            }), "停用虚拟仓失败");
            if (!result.Success)
            {
                _messages.ShowError("虚拟仓未停用", result.Message);
                return;
            }
            _writeIntents.Remove(intentKey);
            var refreshed = await LoadCoreAsync(target.Uid) == ListLoadResult.Succeeded;
            _messages.ShowSuccess("虚拟仓已停用", target.Code);
            if (!refreshed) _messages.ShowWarning("停用成功，但列表刷新失败", ResultSummary ?? "请稍后刷新列表。");
            await RefreshWarehousePickerCacheAfterWriteAsync("停用成功，缓存刷新失败");
        }, "正在停用虚拟仓...");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var target = SelectedItem;
        if (target is null) return;
        var reason = await _reasonDialog.RequestAsync("删除", target);
        if (reason is null) return;
        if (SelectedItem?.Uid != target.Uid || SelectedItem.Version != target.Version)
        {
            _messages.ShowWarning("未执行删除", "确认期间所选虚拟仓已经变化，请重新选择。");
            return;
        }
        var intentKey = CreateIntentKey("delete", target, reason);
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.DeleteAsync(target.Uid, new DeleteWarehouseSiteCommand
            {
                CommandId = GetOrCreateIntent(intentKey), ExpectedVersion = target.Version, Reason = reason
            }), "删除虚拟仓失败");
            if (!result.Success)
            {
                _messages.ShowError("虚拟仓未删除", result.Message);
                return;
            }
            _writeIntents.Remove(intentKey);
            if (Items.Count == 1 && PageIndex > 1) PageIndex--;
            var refreshed = await LoadCoreAsync(null) == ListLoadResult.Succeeded;
            _messages.ShowSuccess("虚拟仓已删除", target.Code);
            if (!refreshed) _messages.ShowWarning("删除成功，但列表刷新失败", ResultSummary ?? "请稍后刷新列表。");
            await RefreshWarehousePickerCacheAfterWriteAsync("删除成功，缓存刷新失败");
        }, "正在删除虚拟仓...");
    }

    /// <summary>在写命令已经成功后独立刷新选择器快照；缓存失败只提示，不改变已完成的业务结果。</summary>
    private async Task RefreshWarehousePickerCacheAfterWriteAsync(string failureTitle)
    {
        try
        {
            var cacheResult = await _referenceDataCache.RefreshWarehouseSitesAsync();
            if (!cacheResult.Success)
                _messages.ShowWarning(failureTitle, cacheResult.Message);
        }
        catch (Exception exception)
        {
            _messages.ShowWarning(failureTitle, exception.Message);
        }
    }

    /// <summary>加载列表并由当前用户操作持有 Busy；实际查询使用请求身份和页面存活双重校验。</summary>
    private async Task<bool> LoadWithBusyAsync(Guid? preferredId, string busyMessage)
    {
        var loadResult = ListLoadResult.Ignored;
        await RunBusyAsync(async () => loadResult = await LoadCoreAsync(preferredId), busyMessage, false);
        if (loadResult == ListLoadResult.Failed && _isAlive && !string.IsNullOrWhiteSpace(ResultSummary))
            _messages.ShowError("虚拟仓列表未更新", ResultSummary);
        return loadResult == ListLoadResult.Succeeded;
    }

    /// <summary>执行可替代列表查询；写成功后的刷新可直接复用，避免嵌套 Busy。</summary>
    private async Task<ListLoadResult> LoadCoreAsync(Guid? preferredId)
    {
        _listCancellation?.Cancel();
        _listCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _listCancellation = cancellation;
        var requestVersion = ++_listRequestVersion;
        var selectedId = preferredId ?? SelectedItem?.Uid;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(new WarehouseSiteSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                SupplierId = SearchSupplier?.Uid, IsActive = SelectedStatus.Value,
                PageIndex = Math.Max(1, PageIndex), PageSize = Math.Clamp(PageSize, 1, 200)
            }, cancellation.Token), cancellation.Token, "查询虚拟仓失败");
            if (!CanAcceptListResult(cancellation, requestVersion)) return ListLoadResult.Ignored;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = string.IsNullOrWhiteSpace(result.Message) ? "虚拟仓查询失败。" : result.Message;
                return ListLoadResult.Failed;
            }
            Items.Clear();
            foreach (var item in result.Data.Items) Items.Add(item);
            TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex;
            PageSize = result.Data.PageSize;
            SelectedItem = Items.FirstOrDefault(item => item.Uid == selectedId) ?? Items.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 个虚拟仓，本页 {Items.Count} 条。";
            return ListLoadResult.Succeeded;
        }
        finally
        {
            if (ReferenceEquals(_listCancellation, cancellation)) _listCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>关闭或替换抽屉前保护未保存内容；写命令期间拒绝关闭。</summary>
    private async Task<bool> PrepareDrawerReplacementAsync()
    {
        if (!IsDrawerOpen) return true;
        if (IsBusy) return false;
        if (EditModel?.HasChanges == true && !await _dialogs.ConfirmAsync(
                "放弃虚拟仓修改", "当前抽屉有未保存修改，继续将放弃这些内容。", "放弃修改"))
            return false;
        CloseDrawerCore();
        return true;
    }

    /// <summary>只在已经通过未保存检查或明确保存成功后清理抽屉状态。</summary>
    private void CloseDrawerCore()
    {
        IsDrawerOpen = false;
        EditModel = null;
        IsDirty = false;
    }

    private bool CanAcceptListResult(CancellationTokenSource source, int version) =>
        _isAlive && !source.IsCancellationRequested && ReferenceEquals(_listCancellation, source) && _listRequestVersion == version;

    private Guid GetOrCreateIntent(string key)
    {
        if (_writeIntents.TryGetValue(key, out var commandId)) return commandId;
        commandId = Guid.NewGuid();
        _writeIntents[key] = commandId;
        return commandId;
    }

    private static string CreateIntentKey(string operation, WarehouseSiteDto item, string reason) =>
        $"{operation}|{item.Uid:N}|{item.Version}|{reason.Trim()}";

    private bool CanOpenCreate() => HasManagePermission && IsNotBusy && !IsDrawerOpen;
    private bool CanOpenEdit() => HasManagePermission && IsNotBusy && SelectedItem is not null;
    private bool CanSave() => HasManagePermission && IsNotBusy && IsDrawerOpen && EditModel?.HasChanges == true;
    private bool CanCloseDrawer() => IsDrawerOpen && IsNotBusy;
    private bool CanDeactivate() => HasManagePermission && IsNotBusy && !IsDrawerOpen && SelectedItem?.IsActive == true;
    private bool CanDelete() => HasManagePermission && IsNotBusy && !IsDrawerOpen && SelectedItem is not null;

    private void OnEditModelPropertyChanged(object? sender, PropertyChangedEventArgs args) => UpdateEditState();

    private void UpdateEditState()
    {
        IsDirty = EditModel?.HasChanges == true;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnCurrentUserChanged(object? sender, EventArgs args) => ApplyPermissions();

    /// <summary>根据会话权限更新页面操作能力；后端仍会独立重验。</summary>
    private void ApplyPermissions()
    {
        HasManagePermission = _currentUser.HasPermission(PermissionKeys.InventoryWarehouseManage);
        IsReadOnly = !HasManagePermission;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_isAlive) return;
        _isAlive = false;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        if (EditModel is not null) EditModel.PropertyChanged -= OnEditModelPropertyChanged;
        CancelAndDispose(ref _listCancellation);
        CancelAndDispose(ref _detailCancellation);
    }

    /// <summary>取消并释放页面拥有的查询资源；重复释放安全。</summary>
    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var current = source;
        source = null;
        current?.Cancel();
        current?.Dispose();
    }
}

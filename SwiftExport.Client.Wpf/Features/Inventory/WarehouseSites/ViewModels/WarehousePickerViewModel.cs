using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

/// <summary>编排一次性活动仓库只读单选会话，并基于共享完整快照执行本地搜索与分页。</summary>
public sealed partial class WarehousePickerViewModel : ViewModelBaseEx, IDialogContentState, IDialogCloseRequest, IDisposable
{
    private const int DefaultPageSize = 20;
    private readonly IReferenceDataCacheService? _cache;
    private readonly Func<CancellationToken, Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>>>? _loadOptions;
    private IReadOnlyList<WarehouseSitePickerDto> _options = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _disposed;

    /// <summary>创建一次性仓库选择会话；每次弹窗使用独立实例。</summary>
    public WarehousePickerViewModel(IReferenceDataCacheService cache)
    {
        _cache = cache;
        Title = "选择仓库";
        IsReadOnly = true;
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(ConfirmCommand);
    }

    /// <summary>为业务专用权限提供会话级候选供数；复用原选择交互，不接触仓库管理共享缓存。</summary>
    public WarehousePickerViewModel(Func<CancellationToken, Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>>> loadOptions)
        : this((IReferenceDataCacheService)null!) => _loadOptions = loadOptions;

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    /// <summary>获取当前本地分页结果。</summary>
    public ObservableCollection<WarehouseSitePickerDto> Items { get; } = [];

    /// <summary>获取总页数。</summary>
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

    /// <summary>获取 PageNavigator 左侧状态摘要。</summary>
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页，共 {TotalCount} 条 · {ResultSummary}";

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = DefaultPageSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial string ResultSummary { get; set; } = "尚未加载。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial WarehouseSitePickerDto? SelectedItem { get; set; }

    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => CanConfirm();

    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;

    /// <inheritdoc/>
    public bool CanClose(DialogResult requestedResult) => true;

    /// <summary>按需确保当前会话拥有完整仓库快照；释放后迟到结果不得回写页面。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        using var cancellationRegistration = cancellationToken.Register(_lifetimeCancellation.Cancel);
        ResultSummary = "正在加载仓库...";
        var result = await LoadOptionsAsync(false);
        if (_disposed || _lifetimeCancellation.IsCancellationRequested) return;

        ApplyFilter(resetPage: true);
        if (!result.Success) ResultSummary = result.Message;
    }

    /// <summary>返回当前页仍有效的选中项；取消或过期选择返回空值。</summary>
    public WarehouseSitePickerDto? GetSelectedResult() => CanConfirm() ? SelectedItem : null;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Search() => ApplyFilter(resetPage: true);

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            RaiseDialogStateChanged();
            var result = await LoadOptionsAsync(true);
            if (_disposed || _lifetimeCancellation.IsCancellationRequested) return;
            ApplyFilter(resetPage: true);
            ResultSummary = result.Success ? "仓库缓存已刷新。" : result.Message;
        }, "正在刷新仓库...");
        RaiseDialogStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChangePage))]
    private void PageChange(PageChangeRequest? request)
    {
        if (request is null) return;
        PageSize = Math.Clamp(request.NewSize <= 0 ? DefaultPageSize : request.NewSize, 1, 200);
        PageIndex = Math.Max(1, request.NewIndex);
        ApplyFilter(resetPage: false);
    }

    /// <summary>使用与外壳主按钮相同的准入状态请求确认，供有效数据行双击复用。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(DialogResult.Primary));

    private bool CanInteract() => !_disposed && !IsBusy;

    private bool CanChangePage(PageChangeRequest? request) => CanInteract() && request is not null;

    private bool CanConfirm() => !_disposed && !IsBusy && SelectedItem is not null && Items.Contains(SelectedItem);

    /// <summary>从共享只读快照筛选代码与名称，并在每次查询或换页时清除旧选择。</summary>
    private void ApplyFilter(bool resetPage)
    {
        if (_disposed) return;
        if (resetPage) PageIndex = 1;
        SelectedItem = null;

        var keyword = Keyword.Trim();
        var filtered = _options.Where(item =>
            string.IsNullOrEmpty(keyword)
            || Contains(item.Code, keyword)
            || Contains(item.Name, keyword)
            || Contains(item.SupplierCode, keyword)
            || Contains(item.SupplierName, keyword)).ToArray();

        TotalCount = filtered.Length;
        PageIndex = Math.Clamp(PageIndex, 1, TotalPages);
        var pageSize = Math.Clamp(PageSize, 1, 200);
        Items.Clear();
        foreach (var item in filtered.Skip((PageIndex - 1) * pageSize).Take(pageSize))
            Items.Add(item);

        ResultSummary = Items.Count == 0
            ? "没有符合条件的活动仓库。"
            : $"当前页显示 {Items.Count} 个活动仓库。";
        PageChangeCommand.NotifyCanExecuteChanged();
        RaiseDialogStateChanged();
    }

    private static bool Contains(string? source, string keyword) =>
        !string.IsNullOrEmpty(source) && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>业务供数只保存在本弹窗；原入口继续复用共享缓存及其既有刷新协议。</summary>
    private async Task<CacheRefreshResult> LoadOptionsAsync(bool refresh)
    {
        if (_loadOptions is not null)
        {
            var loaded = await _loadOptions(_lifetimeCancellation.Token);
            if (!_disposed && !_lifetimeCancellation.IsCancellationRequested && loaded.Success && loaded.Data is not null) _options = loaded.Data;
            return loaded.Success ? CacheRefreshResult.Ok() : CacheRefreshResult.Fail(loaded.Message);
        }
        var result = refresh ? await _cache!.RefreshWarehouseSitesAsync() : await _cache!.EnsureWarehouseSitesLoadedAsync();
        if (!_disposed && !_lifetimeCancellation.IsCancellationRequested) _options = _cache.WarehouseSites.ToArray();
        return result;
    }

    partial void OnSelectedItemChanged(WarehouseSitePickerDto? value) => RaiseDialogStateChanged();

    private void RaiseDialogStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        SelectedItem = null;
        Items.Clear();
        RaiseDialogStateChanged();
    }
}

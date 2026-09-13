using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

/// <summary>编排活动仓库的只读单选、分页查询和弹窗生命周期，不修改来源仓库资料。</summary>
public sealed partial class WarehousePickerViewModel : ViewModelBaseEx, IDialogContentState, IDisposable
{
    private const int DefaultPageSize = 20;
    private readonly IWarehouseSiteApi _api;
    private readonly IApiCallRunner _runner;
    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _loadCancellation;
    private int _requestVersion;
    private bool _isOpen;
    private bool _hasCurrentResult;

    /// <summary>创建仓库选择器并注册受 Busy 状态约束的分页命令。</summary>
    public WarehousePickerViewModel(IWarehouseSiteApi api, IApiCallRunner runner)
    {
        _api = api;
        _runner = runner;
        Title = "选择仓库";
        IsReadOnly = true;
        RegisterBusyAwareCommand(PreviousPageCommand);
        RegisterBusyAwareCommand(NextPageCommand);
    }

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <summary>获取当前页候选。</summary>
    public ObservableCollection<WarehouseSitePickerDto> Items { get; } = [];

    /// <summary>获取总页数。</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));

    /// <summary>获取分页摘要。</summary>
    public string PageSummary => $"第 {PageIndex} / {TotalPages} 页，共 {TotalCount} 条";

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

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
    public partial string ResultSummary { get; set; } = "尚未加载。";

    [ObservableProperty]
    public partial WarehouseSitePickerDto? SelectedItem { get; set; }

    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => _isOpen && _hasCurrentResult && !IsBusy
        && SelectedItem is not null && Items.Contains(SelectedItem);

    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;

    /// <inheritdoc/>
    public bool CanClose(DialogResult requestedResult) => true;

    /// <summary>打开一个新的选择会话并加载第一页；外部取消会终止本会话的所有查询。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        Close();
        _isOpen = true;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        PageIndex = 1;
        SelectedItem = null;
        Items.Clear();
        TotalCount = 0;
        ResultSummary = "正在加载仓库...";
        RaiseDialogStateChanged();
        await LoadAsync();
    }

    /// <summary>返回当前页中仍有效的选中项；过期选择不会穿透弹窗边界。</summary>
    public WarehouseSitePickerDto? GetSelectedResult()
        => IsPrimaryButtonEnabled ? SelectedItem : null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        PageIndex = 1;
        await LoadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync()
    {
        PageIndex--;
        await LoadAsync();
    }

    private bool CanPreviousPage() => _isOpen && !IsBusy && PageIndex > 1;

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync()
    {
        PageIndex++;
        await LoadAsync();
    }

    private bool CanNextPage() => _isOpen && !IsBusy && PageIndex < TotalPages;

    /// <summary>执行可替代查询；仅当前会话中的最后一个响应可以回写集合。</summary>
    private async Task LoadAsync()
    {
        if (!_isOpen || _sessionCancellation is null) return;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);
        _loadCancellation = cancellation;
        var requestVersion = ++_requestVersion;
        _hasCurrentResult = false;
        SelectedItem = null;
        Items.Clear();
        TotalCount = 0;
        ResultSummary = "正在查询仓库...";
        RaiseDialogStateChanged();

        ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>? result = null;
        await RunBusyAsync(async () =>
        {
            RaiseDialogStateChanged();
            result = await _runner.RunAsync(
                () => _api.GetWarehousePickerPagedAsync(new PagedRequestBase
                {
                    Keyword = string.IsNullOrWhiteSpace(Keyword) ? string.Empty : Keyword.Trim(),
                    PageIndex = Math.Max(1, PageIndex),
                    PageSize = Math.Clamp(PageSize, 1, 200)
                }, cancellation.Token), cancellation.Token, "查询仓库失败");
        }, "正在查询仓库...", false);

        if (!CanAccept(cancellation, requestVersion))
        {
            cancellation.Dispose();
            return;
        }

        _loadCancellation = null;
        cancellation.Dispose();
        if (result is null || !result.Success || result.Data is null)
        {
            PageIndex = 1;
            ResultSummary = result?.Message ?? "仓库查询失败。";
            RaiseDialogStateChanged();
            return;
        }

        Items.Clear();
        foreach (var item in result.Data.Items) Items.Add(item);
        PageIndex = Math.Max(1, result.Data.PageIndex);
        PageSize = Math.Max(1, result.Data.PageSize);
        TotalCount = Math.Max(0, result.Data.TotalCount);
        _hasCurrentResult = true;
        ResultSummary = Items.Count == 0 ? "没有符合条件的活动仓库。" : $"当前页显示 {Items.Count} 个活动仓库。";
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        RaiseDialogStateChanged();
    }

    private bool CanAccept(CancellationTokenSource cancellation, int requestVersion)
        => _isOpen && !cancellation.IsCancellationRequested
            && ReferenceEquals(_loadCancellation, cancellation) && requestVersion == _requestVersion;

    partial void OnSelectedItemChanged(WarehouseSitePickerDto? value) => RaiseDialogStateChanged();

    partial void OnPageIndexChanged(int value)
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalCountChanged(int value) => NextPageCommand.NotifyCanExecuteChanged();

    /// <summary>关闭会话并取消所有未完成查询，防止迟到响应污染下一次打开。</summary>
    public void Close()
    {
        _isOpen = false;
        _hasCurrentResult = false;
        _requestVersion++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        SelectedItem = null;
        RaiseDialogStateChanged();
    }

    private void RaiseDialogStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void Dispose() => Close();
}

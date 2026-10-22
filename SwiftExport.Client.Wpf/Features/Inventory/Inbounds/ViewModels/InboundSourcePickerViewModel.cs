using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Features.Inventory.Services;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>按固定类型分页选源，不按仓库筛选；只返回查询快照，不检查业务并发，不预占库存。</summary>
public partial class InboundSourcePickerViewModel : ObservableObject, IDialogContentState, IDisposable
{
    private readonly IInboundSourceApi? _api;
    private readonly IApiCallRunner? _runner;
    private CancellationTokenSource? _supplierOptionsCancellation;
    private bool _initialized;
    private CancellationTokenSource? _query;
    private bool _disposed;
    private bool _hasResult;
    private long _requestId;
    private InboundSourceSearchRequest? _appliedSearch;
    private Guid? _currentInboundDocumentId;
    private string? _inputWarning;

    /// <summary>仅供设计器及本地模型测试创建，不提供任何远程数据。</summary>
    public InboundSourcePickerViewModel() { }

    /// <summary>运行时注入入库权限下的只读 API，不依赖其他主数据权限或全量缓存。</summary>
    public InboundSourcePickerViewModel(IInboundSourceApi api, IApiCallRunner runner)
    { _api = api; _runner = runner; }
    /// <summary>通知弹窗刷新确认按钮状态。</summary>
    public event EventHandler? StateChanged;

    /// <summary>入库查看权限下完整读取的未删除供应商身份，不按有无采购记录缩小。</summary>
    public System.Collections.ObjectModel.ObservableCollection<SupplierDto> Suppliers { get; } = [];
    /// <summary>仅采购类型显示的两种分类，默认客供件。</summary>
    public IReadOnlyList<InboundPurchaseCategoryEnum> PurchaseCategories { get; } = [InboundPurchaseCategoryEnum.CustomerSupplied, InboundPurchaseCategoryEnum.Normal];
    /// <summary>弹窗所属业务类型，由调用方固定，不能在选源中改变。</summary>
    public InboundDocumentTypeEnum DocumentType { get; private set; } = InboundDocumentTypeEnum.PurchaseReceipt;
    /// <summary>生产来源未实现，不能套用采购筛选或接口。</summary>
    public bool IsPurchase => DocumentType == InboundDocumentTypeEnum.PurchaseReceipt;

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial InboundPurchaseCategoryEnum PurchaseCategory { get; set; } = InboundPurchaseCategoryEnum.CustomerSupplied;
    [ObservableProperty] public partial IReadOnlyList<InboundSourcePickerRowModel> Items { get; private set; } = Array.Empty<InboundSourcePickerRowModel>();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial int PageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; private set; }
    /// <summary>选源按服务端分页加载，避免一次获取全部采购行。</summary>
    public int PageSize => 50;
    [ObservableProperty] public partial string StatusText { get; private set; } = "修改条件后点击查询；已有勾选请先确认或清空。";
    /// <summary>保留上次成功结果可供选择，仅查询加载或页面释放期间禁用。</summary>
    public bool CanSelect => !_disposed && !IsLoading && _hasResult;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => CanSelect && Items.Any(x => x.IsChecked);
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <summary>当前页全部行是否勾选。</summary>
    public bool IsAllChecked => Items.Count > 0 && Items.All(x => x.IsChecked);
    /// <summary>当前页选中数量。</summary>
    public int CheckedCount => Items.Count(x => x.IsChecked);
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;

    /// <summary>初始化固定上下文与供应商候选；候选失败仍查询无供应商条件的第一页。</summary>
    public async Task InitializeAsync(InboundSourcePickerRequest request, CancellationToken cancellationToken)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        _currentInboundDocumentId = request.CurrentInboundDocumentId;
        DocumentType = request.DocumentType;
        OnPropertyChanged(nameof(IsPurchase));
        if (!IsPurchase)
        { StatusText = "该入库类型的选源尚未实现。"; return; }
        string? supplierError = null;
        if (_api is not null && _runner is not null)
        {
            using var options = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _supplierOptionsCancellation = options;
            try
            {
                var loaded = await InventorySupplierOptionsLoader.LoadAsync(_api.GetSupplierOptionsAsync, _runner, InboundQueryLimits.MaxPageSize, options.Token);
                if (_disposed || options.IsCancellationRequested) return;
                if (loaded.Success && loaded.Data is not null) foreach (var row in loaded.Data) Suppliers.Add(row);
                else supplierError = loaded.Message;
            }
            catch (OperationCanceledException) when (options.IsCancellationRequested) { return; }
            finally { if (ReferenceEquals(_supplierOptionsCancellation, options)) _supplierOptionsCancellation = null; }
        }
        await QueryAsync(1, cancellationToken);
        if (!_disposed && !cancellationToken.IsCancellationRequested && supplierError is not null)
            StatusText += $" 供应商候选未加载：{supplierError}；可不选供应商继续查询。";
    }

    /// <summary>显式查询第一页，不重新获取有效采购供应商名单。</summary>
    [RelayCommand(CanExecute = nameof(CanSearch), AllowConcurrentExecutions = true)]
    private Task SearchAsync(CancellationToken token) => QueryAsync(1, token);

    /// <summary>翻页重新加载快照，当前页勾选不被误当作跨页选择。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(int page, CancellationToken token) => QueryAsync(page, token, applyEditedFilters: false);

    /// <summary>可取消且 latest-request-wins；这是 UI 请求防串回写，不是业务并发检查。</summary>
    private async Task QueryAsync(int page, CancellationToken token, bool applyEditedFilters = true)
    {
        if (_disposed || !IsPurchase || _api is null || _runner is null) return;
        if (Items.Any(x => x.IsChecked))
        {
            StatusText = "已有勾选，请先确认加入 Manager，或点击清空勾选后再查询/翻页。";
            return;
        }
        // 输入框是待查询条件；翻页继续使用当前结果的条件，避免未点查询就隐式切换筛选。
        var applied = applyEditedFilters ? null : _appliedSearch;
        var request = new InboundSourceSearchRequest
        {
            DocumentType = DocumentType,
            CurrentInboundDocumentId = _currentInboundDocumentId,
            PurchaseCategory = applied is null ? PurchaseCategory : applied.PurchaseCategory,
            SupplierId = applied is null ? SelectedSupplier?.Uid : applied.SupplierId,
            Keyword = applied is null ? Keyword?.Trim() : applied.Keyword,
            PageIndex = page,
            PageSize = PageSize
        };
        if (GetInputError(request.Keyword, request.PageIndex, request.PageSize) is { } error)
        { _inputWarning = error; StatusText = error; return; }
        _query?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(token);
        _query = query;
        var requestId = ++_requestId;
        IsLoading = true; NotifySelection();
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(request, query.Token),
                query.Token, "入库来源查询失败，请重试。");

            if (_disposed || requestId != _requestId || query.IsCancellationRequested) return;

            if (!result.Success || result.Data is null)
            { StatusText = result.Message ?? "来源查询失败。"; return; }

            if (result.Data.Items.Any(x => x.DocumentType != DocumentType || x.SourceLineId == Guid.Empty))
            { StatusText = "来源响应的类型或行标识不符合本次查询，未接纳数据。"; return; }
            LoadItems(result.Data.Items.Adapt<List<InboundSourcePickerRowModel>>());
            _appliedSearch = request;
            PageIndex = page; TotalCount = result.Data.TotalCount; _hasResult = true;
            StatusText = Items.Count == 0 ? "没有符合条件的采购来源。" : "修改条件后点击查询；仅当前页勾选有效，换页前请先确认或清空勾选。";
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("入库选源查询失败：{0}", ex.GetType().Name);
            if (!_disposed && requestId == _requestId) StatusText = "来源查询失败，请重试。";
        }
        finally
        {
            if (ReferenceEquals(_query, query)) { _query = null; IsLoading = false; NotifySelection(); }
        }
    }

    /// <summary>替换当前页并对称管理勾选事件；设计器也复用此入口。</summary>
    public void LoadItems(IEnumerable<InboundSourcePickerRowModel> items)
    {
        var rows = items.ToArray();
        foreach (var row in Items) row.PropertyChanged -= OnRowChanged;
        Items = rows;
        foreach (var row in Items) row.PropertyChanged += OnRowChanged;
        _hasResult = true;
        NotifySelection();
    }
    /// <summary>读取选中行，保留原始来源数量，不执行版本复核。</summary>
    public IReadOnlyList<InboundSourcePickerRowModel> GetCheckedItems() => Items.Where(x => x.IsChecked).ToArray();
    /// <summary>确认时创建独立传输快照，不把可变行模型交给 Manager。</summary>
    public IReadOnlyList<InboundSourceCandidateDto> GetSelectedResult() => GetCheckedItems().Adapt<List<InboundSourceCandidateDto>>();

    /// <summary>表头切换当前页全选或取消。</summary>
    [RelayCommand]
    private void ToggleAll()
    { var check = !IsAllChecked; foreach (var item in Items) item.IsChecked = check; }

    /// <summary>逐行反转当前页勾选，与全选/取消分开。</summary>
    [RelayCommand]
    private void InvertSelection()
    { foreach (var item in Items) item.IsChecked = !item.IsChecked; }

    /// <summary>显式放弃当前页勾选后才允许换页或重新搜索，避免静默丢失选择。</summary>
    [RelayCommand]
    private void ClearSelection()
    { foreach (var item in Items) item.IsChecked = false; }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(InboundSourcePickerRowModel.IsChecked)) NotifySelection(); }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(IsAllChecked)); OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(IsPrimaryButtonEnabled)); OnPropertyChanged(nameof(CanSelect));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSearch() => !_disposed && IsPurchase && GetInputError(Keyword, 1, PageSize) is null;

    /// <summary>前端与后端共用查询边界，拒绝无效请求时保留已接纳结果及分页事实。</summary>
    private static string? GetInputError(string? keyword, int page, int size) => keyword?.Trim().Length > InboundQueryLimits.KeywordMaxLength
        ? $"查询关键词最多 {InboundQueryLimits.KeywordMaxLength} 字。"
        : page < 1 || size is < 1 or > InboundQueryLimits.MaxPageSize ? $"页码须为正数，每页最多 {InboundQueryLimits.MaxPageSize} 条。" : null;

    /// <summary>纠正输入只清除本地错误，不覆盖之后出现的查询结果或反馈。</summary>
    partial void OnKeywordChanged(string value)
    {
        var error = GetInputError(value, 1, PageSize);
        if (error is not null) { _inputWarning = error; StatusText = error; }
        else { if (_inputWarning is not null && StatusText == _inputWarning) StatusText = "查询条件已修正，请点击查询。"; _inputWarning = null; }
        SearchCommand.NotifyCanExecuteChanged();
    }
    /// <summary>关闭或取消后停止查询，释放行订阅并拒绝晚到结果。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _requestId++; _query?.Cancel(); _supplierOptionsCancellation?.Cancel();
        foreach (var row in Items) row.PropertyChanged -= OnRowChanged;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Caching;
using SwiftExport.Contracts.DTOs.Customers;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>管理只读列表、独立详情页签和整单草稿删除。</summary>
public sealed partial class PackingListManagerViewModel(IPackingListApi api, IApiCallRunner runner,
    INavigationService navigation, IDialogService dialogs, IMessageService messages, IReferenceDataCacheService cache)
    : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private CancellationTokenSource? _query, _detailQuery;
    private long _queryId, _detailId;
    private bool _disposed, _initialized, _confirming;
    private PackingListManagerSearchRequest? _applied;
    private readonly Dictionary<(Guid, int), Guid> _deleteCommands = [];
    /// <summary>管理页固定路由。</summary>
    public const string RouteKey = "shipping.packing-list-manager";
    public PackingListManagerSearchModel Search { get; } = new();
    /// <summary>复用当前会话客户缓存作为查询筛选，不改变客户资料。</summary>
    public ReadOnlyObservableCollection<CustomerDto> Customers => cache.Customers;
    public PackingListManagerListModel Headers { get; } = new();
    public PackingListLineListModel Lines { get; } = new();
    [ObservableProperty] public partial PackingListManagerRowModel? SelectedHeader { get; set; }
    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string ResultSummary { get; private set; } = "查询装箱单草稿与交付状态。";
    public IReadOnlyList<int> PageSizeOptions { get; } = [20, 50, 100];
    public string PageStatus => $"第 {Search.PageIndex} 页，共 {TotalCount} 张";
    /// <inheritdoc/>
    public string DocumentTitle => "PackingList 工作台";
    /// <inheritdoc/>
    public bool HasUnsavedChanges => false;
    /// <inheritdoc/>
    public Task<bool> CanCloseAsync(CancellationToken token) => Task.FromResult(!IsBusy && !_confirming);
    /// <inheritdoc/>
    public Task InitializeAsync() { if (_initialized) return Task.CompletedTask; _initialized = true; return QueryAsync(1, true); }
    partial void OnSelectedHeaderChanged(PackingListManagerRowModel? value) => _ = LoadLinesAsync(value);
    [RelayCommand(AllowConcurrentExecutions = true)] private Task SearchAsync() => QueryAsync(1, true);
    [RelayCommand(AllowConcurrentExecutions = true)] private Task RefreshAsync() => QueryAsync(Search.PageIndex, false);
    [RelayCommand] private void ClearFilters() { Search.PostingStatus = null; Search.Keyword = null; Search.CustomerId = null; Search.InvoiceDateFrom = null; Search.InvoiceDateTo = null; }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request)
    { if (request is null) return Task.CompletedTask; return QueryAsync(request.NewIndex, false, request.NewSize); }
    [RelayCommand]
    private Task AddNewAsync()
    {
        if (_disposed || IsBusy || _confirming) return Task.CompletedTask;
        var uid = Guid.NewGuid();
        return navigation.OpenDocumentAsync(PackingListDetailViewModel.RouteKey, uid.ToString("N"), new PackingListOpenRequest { Uid = uid, IsNew = true });
    }
    [RelayCommand]
    private Task EditAsync() => _disposed || IsBusy || _confirming || SelectedHeader is null ? Task.CompletedTask :
        navigation.OpenDocumentAsync(PackingListDetailViewModel.RouteKey, SelectedHeader.Uid.ToString("N"), new PackingListOpenRequest { Uid = SelectedHeader.Uid });
    /// <summary>在独立详情中核对、保存和预览，不从只读管理列表直接提交库存。</summary>
    [RelayCommand] private Task PostAsync()
    {
        if (SelectedHeader == null) { messages.ShowWarning("PackingList", "请先选择箱单。"); return Task.CompletedTask; }
        return EditAsync();
    }
    /// <summary>在详情中按原执行记录确认整单反向。</summary>
    [RelayCommand] private Task ReverseAsync() => EditAsync();
    /// <summary>确认后复核目标；同版本删除重试复用命令身份。</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_disposed || IsBusy || _confirming || SelectedHeader is not { } row) return;
        if (row.LifecycleStatus != PackingListLifecycleStatusEnum.Active || row.PostingStatus != PackingListPostingStatusEnum.Unposted || row.IsDeleted)
        { ValidationSummary = "仅可删除有效未过账草稿。"; return; }
        _confirming = true; bool confirmed;
        try { confirmed = await dialogs.ConfirmAsync("删除草稿", $"确定删除 {row.PackingListNo} 整张草稿？"); }
        catch (Exception ex) { await HandleExceptionAsync(ex); return; }
        finally { _confirming = false; }
        if (!confirmed || _disposed || IsBusy || !ReferenceEquals(row, SelectedHeader)) return;
        await RunBusyAsync(async () =>
        {
            _query?.Cancel(); _detailQuery?.Cancel(); ++_queryId; ++_detailId;
            var key = (row.Uid, row.Version);
            if (!_deleteCommands.TryGetValue(key, out var id)) _deleteCommands[key] = id = Guid.NewGuid();
            var result = await runner.RunAsync(() => api.DeleteAsync(row.Uid,
                new DeletePackingListDraftCommand { CommandId = id, ExpectedVersion = row.Version }), "删除失败");
            if (_disposed) return;
            if (!result.Success || result.Data?.Uid != row.Uid) { ValidationSummary = result.Success ? "删除响应无效。" : result.Message; return; }
            Headers.RemoveCompletely(row); SelectedHeader = null; TotalCount = Math.Max(0, TotalCount - 1);
            ValidationSummary = null; ResultSummary = "草稿已删除。"; OnPropertyChanged(nameof(PageStatus));
        }, "删除草稿…");
    }
    /// <summary>替代型查询同时检查取消和请求身份，失败保留当前结果。</summary>
    private async Task QueryAsync(int page, bool applyFilters, int? pageSize = null)
    {
        if (_disposed || IsBusy || _confirming) return;
        var request = applyFilters || _applied is null ? Search.Adapt<PackingListManagerSearchRequest>() : _applied.Adapt<PackingListManagerSearchRequest>();
        request.PageIndex = Math.Max(1, page); request.PageSize = pageSize ?? Search.PageSize;
        if (request.InvoiceDateFrom > request.InvoiceDateTo) { ValidationSummary = "开始日期不能晚于结束日期。"; return; }
        _query?.Cancel(); using var query = new CancellationTokenSource(); _query = query;
        var id = ++_queryId; IsLoading = true;
        try
        {
            var result = await runner.RunAsync(() => api.SearchAsync(request, query.Token), query.Token, "箱单查询失败");
            if (_disposed || id != _queryId || query.IsCancellationRequested) return;
            if (!result.Success || result.Data is null) { ValidationSummary = result.Message; return; }
            var selectedId = SelectedHeader?.Uid;
            SelectedHeader = null; Headers.LoadFromDtos(result.Data.Items);
            _applied = request; Search.PageIndex = request.PageIndex; Search.PageSize = request.PageSize; TotalCount = result.Data.TotalCount;
            SelectedHeader = Headers.Items.FirstOrDefault(x => x.Uid == selectedId) ?? Headers.Items.FirstOrDefault();
            ValidationSummary = null; ResultSummary = "查询完成。"; OnPropertyChanged(nameof(PageStatus));
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && id == _queryId) await HandleExceptionAsync(ex); }
        finally { if (ReferenceEquals(_query, query)) { _query = null; IsLoading = false; } }
    }
    private async Task LoadLinesAsync(PackingListManagerRowModel? row)
    {
        _detailQuery?.Cancel(); var id = ++_detailId; Lines.Clear();
        if (_disposed || row is null) return;
        using var query = new CancellationTokenSource(); _detailQuery = query;
        try
        {
            var result = await runner.RunAsync(() => api.GetAsync(row.Uid, query.Token), query.Token, "箱单明细查询失败");
            if (_disposed || query.IsCancellationRequested || id != _detailId) return;
            if (!result.Success || result.Data?.Header?.Uid != row.Uid) { ValidationSummary = result.Success ? "明细响应身份不符。" : result.Message; return; }
            Lines.LoadFromDtos(result.Data.Lines.OrderBy(x => x.LineNumber));
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && id == _detailId) await HandleExceptionAsync(ex); }
        finally { if (ReferenceEquals(_detailQuery, query)) _detailQuery = null; }
    }
    /// <summary>未知异常不将技术消息直接显示给用户。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { System.Diagnostics.Trace.TraceError("PackingList管理异常：{0}", exception.GetType().Name); ValidationSummary = "操作失败，请重试。"; return Task.CompletedTask; }
    /// <summary>释放查询并阻止关闭后回写。</summary>
    public void Dispose() { _disposed = true; ++_queryId; ++_detailId; _query?.Cancel(); _detailQuery?.Cancel(); }
}

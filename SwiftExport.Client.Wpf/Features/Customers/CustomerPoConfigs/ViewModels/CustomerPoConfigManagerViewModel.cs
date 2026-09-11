using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;

/// <summary>客户采购默认配置管理页模型，编排缓存选择、分页编辑和原子批量保存。</summary>
public sealed partial class CustomerPoConfigManagerViewModel
    : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>获取客户采购默认配置导航路由键。</summary>
    public const string RouteKey = "customer-po-configs";

    private readonly ICustomerPoConfigApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _queryCts;
    private long _querySequence;
    private bool _initialized;
    private bool _disposed;

    /// <summary>初始化页面依赖并连接列表状态。</summary>
    public CustomerPoConfigManagerViewModel(ICustomerPoConfigApi api, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs)
    {
        _api = api;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        Title = "客户采购默认配置";
        Lines.ListStateChanged += OnListStateChanged;
        PropertyChanged += OnManagerPropertyChanged;
        CollectionChangedEventManager.AddHandler(_cache.Currencies, OnCurrenciesChanged);
        RefreshCurrencyOptions();
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
    }

    /// <summary>由视图提交原生 DataGrid 编辑事务。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>获取配置列表。</summary>
    public CustomerPoConfigListModel Lines { get; } = new();
    /// <summary>获取可选择客户缓存。</summary>
    public IEnumerable<CustomerDto> CustomerOptions => _cache.Customers;
    /// <summary>获取可选择供应商缓存。</summary>
    public IEnumerable<SupplierDto> SupplierOptions => _cache.Suppliers;
    /// <summary>获取启用币种、空项及当前历史币种组成的页面选项。</summary>
    [ObservableProperty] public partial IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; set; } = [];
    public string DocumentTitle => Title;
    public bool HasUnsavedChanges => Lines.HasChanges;
    public bool IsInteractionEnabled => !_disposed && IsNotBusy && !IsQuerying && !IsSelectionPrompt;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    /// <summary>获取或设置表格使用格子选择模式还是整行选择模式。</summary>
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerPoConfigLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial bool IsQuerying { get; set; }
    [ObservableProperty] public partial bool IsSelectionPrompt { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载客户采购默认配置。";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = 20;

    partial void OnSelectedLineChanged(CustomerPoConfigLineModel? value) => Lines.SelectedItem = value;

    /// <summary>幂等加载基础资料与第一页配置。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var cacheResult = await _cache.EnsureLoadedAsync();
        OnPropertyChanged(nameof(CustomerOptions));
        OnPropertyChanged(nameof(SupplierOptions));
        RefreshCurrencyOptions();
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        await LoadPageAsync();
    }

    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => EnsureCanDiscardChangesAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        var currencyResult = await _cache.RefreshCurrenciesAsync();
        RefreshCurrencyOptions();
        if (!currencyResult.Success) _messages.ShowWarning("币种缓存刷新失败", currencyResult.Message);
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddRow()
    {
        if (!TryCommitPendingEdits()) return;
        SelectedLine = Lines.AddNewConfig();
        RefreshCurrencyOptions();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!TryCommitPendingEdits()) return;
        var targets = Lines.CheckedItems.Count > 0 ? Lines.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (targets.Length == 0) return;
        if (targets.Any(line => !line.IsNew))
        {
            IsSelectionPrompt = true;
            try
            {
                if (!await _dialogs.ConfirmAsync("删除客户采购默认配置", $"确定删除选中的 {targets.Length} 条配置吗？保存后生效。", "删除")) return;
            }
            finally { IsSelectionPrompt = false; }
        }
        if (_disposed) return;
        var last = Lines.Items.LastOrDefault(line => !targets.Contains(line));
        foreach (var target in targets) Lines.Remove(target);
        SelectedLine = last;
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!TryCommitPendingEdits()) return;
        await RunWriteAsync(async () =>
        {
            var error = await Lines.ValidateListAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("配置未保存", error);
                return;
            }
            var request = Lines.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存客户采购默认配置失败");
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                _messages.ShowError("保存客户采购默认配置失败", result.Message);
                return;
            }
            Lines.ApplySaveResponse(result.Data);
            UpdateChangeState();
            _messages.ShowSuccess("客户采购默认配置已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            if (!await LoadPageAsync()) _messages.ShowWarning("保存成功", "数据已保存，但列表刷新失败。请稍后手动刷新。");
        }, "正在保存客户采购默认配置...");
    }

    [RelayCommand]
    private void RejectListChanges()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return;
        Lines.RejectChanges();
        BindReferenceSelections();
        SelectedLine = Lines.Items.LastOrDefault();
        UpdateChangeState();
    }

    [RelayCommand]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await EnsureCanDiscardChangesAsync()) return;
        PageIndex = Math.Max(1, request.NewIndex);
        PageSize = request.NewSize <= 0 ? 20 : request.NewSize;
        await LoadPageAsync();
    }

    /// <summary>确认客户选择并把稳定业务编码写入当前行。</summary>
    [RelayCommand]
    private void CommitCustomer(CustomerPoConfigLineModel? line)
    {
        if (line?.SelectedCustomer is null) return;
        line.CustomerId = line.SelectedCustomer.Uid;
        line.CustomerCode = line.SelectedCustomer.CustomerCode?.Trim() ?? string.Empty;
    }

    /// <summary>显式清空当前行客户。</summary>
    [RelayCommand]
    private void ClearCustomer(CustomerPoConfigLineModel? line)
    {
        if (line is null) return;
        line.SelectedCustomer = null;
        line.CustomerId = null;
        line.CustomerCode = string.Empty;
    }

    /// <summary>确认供应商选择并把稳定业务编码写入当前行。</summary>
    [RelayCommand]
    private void CommitSupplier(CustomerPoConfigLineModel? line)
    {
        if (line?.SelectedSupplier is null) return;
        line.SupplierId = line.SelectedSupplier.Uid;
        line.SupplierCode = line.SelectedSupplier.SupplierCode.Trim();
    }

    /// <summary>显式清空当前行供应商。</summary>
    [RelayCommand]
    private void ClearSupplier(CustomerPoConfigLineModel? line)
    {
        if (line is null) return;
        line.SelectedSupplier = null;
        line.SupplierId = null;
        line.SupplierCode = string.Empty;
    }

    /// <summary>执行可取消的分页查询，仅最新且页面存活的结果可回写。</summary>
    private async Task<bool> LoadPageAsync()
    {
        var selectedId = SelectedLine?.Uid;
        var sequence = Interlocked.Increment(ref _querySequence);
        var currentCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _queryCts, currentCts);
        previous?.Cancel();
        previous?.Dispose();
        IsQuerying = true;
        try
        {
            var request = new CustomerPoConfigSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize
            };
            var result = await _runner.RunAsync(() => _api.GetPagedAsync(request, currentCts.Token), currentCts.Token, "加载客户采购默认配置失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _querySequence)) return false;
            if (!result.Success)
            {
                ResultSummary = "客户采购默认配置加载失败。";
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载客户采购默认配置失败", result.Message);
                return false;
            }
            var page = result.Data;
            Lines.LoadFromDtos(page?.Items ?? []);
            RefreshCurrencyOptions();
            BindReferenceSelections();
            SelectedLine = Lines.Items.FirstOrDefault(line => line.Uid == selectedId) ?? Lines.Items.FirstOrDefault();
            TotalCount = page?.TotalCount ?? 0;
            PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
            PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 条配置，本页 {Lines.Items.Count} 条。";
            AcceptChanges();
            UpdateChangeState();
            return true;
        }
        finally
        {
            if (ReferenceEquals(_queryCts, currentCts)) IsQuerying = false;
        }
    }

    /// <summary>按现存编码关联缓存选择项；无法匹配时保留原编码供用户修正。</summary>
    private void BindReferenceSelections()
    {
        foreach (var line in Lines.Items)
        {
            using var _ = line.SuppressChangeTracking();
            line.SelectedCustomer = _cache.Customers.FirstOrDefault(item => string.Equals(item.CustomerCode, line.CustomerCode, StringComparison.OrdinalIgnoreCase));
            line.SelectedSupplier = _cache.Suppliers.FirstOrDefault(item => string.Equals(item.SupplierCode, line.SupplierCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>响应应用级币种缓存变化，并保留当前行中已停用或已移除的历史代码。</summary>
    private void OnCurrenciesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCurrencyOptions();

    /// <summary>重建页面币种选项；仅改变选项集合，不写回业务行或制造脏状态。</summary>
    private void RefreshCurrencyOptions()
    {
        var options = new List<SysDicCurrencyDto>
        {
            new()
            {
                Uid = Guid.Empty,
                CurrencyCode = string.Empty,
                CurrencyNameZh = "（空）",
                CurrencyNameEn = string.Empty,
                CurrencySymbol = string.Empty,
                IsEnabled = true
            }
        };
        options.AddRange(_cache.Currencies);

        var knownCodes = options
            .Where(option => !string.IsNullOrWhiteSpace(option.CurrencyCode))
            .Select(option => option.CurrencyCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var code in Lines.Items
                     .Select(line => line.Currency?.Trim())
                     .Where(code => !string.IsNullOrWhiteSpace(code))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!knownCodes.Add(code!)) continue;
            options.Add(new SysDicCurrencyDto
            {
                Uid = Guid.Empty,
                CurrencyCode = code!,
                CurrencyNameZh = "（历史值）",
                CurrencyNameEn = string.Empty,
                CurrencySymbol = string.Empty,
                IsEnabled = false
            });
        }

        CurrencyOptions = options;
    }

    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        if (_disposed || IsBusy || IsSelectionPrompt || !TryCommitPendingEdits()) return false;
        if (!HasUnsavedChanges) return true;
        IsSelectionPrompt = true;
        try { return await _dialogs.ConfirmAsync("放弃未保存变更", "当前配置有未保存修改，继续将放弃这些修改。", "继续"); }
        finally { IsSelectionPrompt = false; }
    }

    private bool TryCommitPendingEdits() => CommitPendingEdits?.Invoke() ?? true;
    private void OnListStateChanged(object? sender, ListStateChangedEventArgs<CustomerPoConfigLineModel> e) => UpdateChangeState();
    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsQuerying) or nameof(IsSelectionPrompt) or nameof(CanWrite))
        {
            OnPropertyChanged(nameof(IsInteractionEnabled));
            AddRowCommand.NotifyCanExecuteChanged();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }
    private bool CanAddRow() => IsInteractionEnabled && CanWrite;
    private bool CanDeleteSelected() => IsInteractionEnabled && CanWrite && (Lines.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsInteractionEnabled && CanWrite && Lines.HasChanges;

    /// <summary>取消未完成查询并解除事件订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lines.ListStateChanged -= OnListStateChanged;
        PropertyChanged -= OnManagerPropertyChanged;
        CollectionChangedEventManager.RemoveHandler(_cache.Currencies, OnCurrenciesChanged);
        CommitPendingEdits = null;
        var cts = Interlocked.Exchange(ref _queryCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}

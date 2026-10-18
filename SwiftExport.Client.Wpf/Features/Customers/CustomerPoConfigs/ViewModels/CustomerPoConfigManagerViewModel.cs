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
using SwiftExportClient.Authentication;

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
    private readonly ICurrentUserService _user;
    private CancellationTokenSource? _queryCts;
    private long _querySequence;
    private bool _initialized;
    private bool _disposed;

    /// <summary>初始化页面依赖并连接列表状态。</summary>
    public CustomerPoConfigManagerViewModel(ICustomerPoConfigApi api, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs, ICurrentUserService currentUser)
    {
        _api = api;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _user = currentUser;
        _user.CurrentUserChanged += OnPermissionsChanged;
        Title = "客户采购默认配置";
        Lines.ListStateChanged += OnListStateChanged;
        PropertyChanged += OnManagerPropertyChanged;
        CollectionChangedEventManager.AddHandler(_cache.Currencies, OnCurrenciesChanged);
        CollectionChangedEventManager.AddHandler(_cache.Customers, OnReferenceCacheChanged);
        CollectionChangedEventManager.AddHandler(_cache.Suppliers, OnReferenceCacheChanged);
        InitializeCheckSelection();
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
    /// <summary>仅有查看权限且没有查询、写入或确认时允许交互。</summary>
    public bool IsInteractionEnabled => CanView && IsNotBusy && !IsQuerying && !IsSelectionPrompt;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    /// <summary>获取或设置表格使用格子选择模式还是整行选择模式。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsCellSelectionMode { get; set; }
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
        if (_initialized || !CanQuery()) return;
        _initialized = true;
        var identity = _user.CurrentUser;
        var cacheResult = await _cache.EnsureLoadedAsync();
        if (!CanView || !ReferenceEquals(identity, _user.CurrentUser)) return;
        OnPropertyChanged(nameof(CustomerOptions));
        OnPropertyChanged(nameof(SupplierOptions));
        RefreshCurrencyOptions();
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        await LoadPageAsync(1, PageSize, CaptureFilters());
    }

    /// <summary>撤销查看权限也允许申请关闭，仍保留原生输入与未保存确认。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => EnsureCanDiscardChangesAsync(requireViewPermission: false);

    /// <summary>提交客户、供应商及关键词快照，取消或失败时恢复成功条件。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => ApplyFiltersAsync(CaptureFilters());

    /// <summary>刷新三项引用缓存后重载已应用范围，查询仍复核当前身份和存活状态。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task RefreshAsync()
    {
        if (!CanQuery() || !await EnsureCanDiscardChangesAsync()) { RestoreFilterEditors(); return; }
        var identity = _user.CurrentUser;
        await RunBusyAsync(async () =>
        {
            var results = await Task.WhenAll(_cache.RefreshCustomersAsync(), _cache.RefreshSuppliersAsync(), _cache.RefreshCurrenciesAsync());
            if (!CanView || !ReferenceEquals(identity, _user.CurrentUser)) return;
            RefreshCurrencyOptions(); BindReferenceSelections(); RestoreFilterEditors();
            if (results.Any(result => !result.Success))
                _messages.ShowWarning("基础资料缓存刷新失败", string.Join("；", results.Where(result => !result.Success).Select(result => result.Message)));
            await LoadPageAsync(PageIndex, PageSize, _appliedFilters);
        }, "正在刷新配置和基础资料...");
    }

    /// <summary>复核创建权限后新增配置；不能用默认 CanWrite 代替独立权限。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddRow()
    {
        if (!CanAddRow() || !TryCommitPendingEdits() || !CanAddRow()) return;
        SelectedLine = Lines.AddNewConfig();
        RefreshCurrencyOptions();
        UpdateChangeState();
    }

    /// <summary>沿用勾选优先的删除目标，确认后再次复核身份、权限及选区快照。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDeleteSelected() || !TryCommitPendingEdits()) return;
        var identity = _user.CurrentUser;
        var targets = DeleteTargets();
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
        if (!CanDeleteSelected() || !ReferenceEquals(identity, _user.CurrentUser) || !targets.SequenceEqual(DeleteTargets())) return;
        var last = Lines.Items.LastOrDefault(line => !targets.Contains(line));
        foreach (var target in targets) Lines.Remove(target);
        SelectedLine = last;
        UpdateChangeState();
    }

    /// <summary>提交原生编辑后按创建、修改和删除分别复核权限；失败或身份变化保留草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !TryCommitPendingEdits() || !CanSave()) return;
        var identity = _user.CurrentUser;
        await RunWriteAsync(async () =>
        {
            var error = await Lines.ValidateListAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error; _messages.ShowWarning("配置未保存", error); return;
            }
            if (!AreChangesAuthorized() || !ReferenceEquals(identity, _user.CurrentUser)) return;
            var request = Lines.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存客户采购默认配置失败");
            if (!AreChangesAuthorized() || !ReferenceEquals(identity, _user.CurrentUser))
            {
                if (!_disposed) ResultSummary = "保存期间身份或权限已变化，请重新查询核对结果；本地草稿已保留。";
                return;
            }
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                _messages.ShowError("保存客户采购默认配置失败", result.Message); return;
            }
            Lines.ApplySaveResponse(result.Data); UpdateChangeState();
            _messages.ShowSuccess("客户采购默认配置已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            if (!await LoadPageAsync(PageIndex, PageSize, _appliedFilters))
                _messages.ShowWarning("保存成功", "数据已保存，但列表刷新失败。请稍后手动刷新。");
        }, "正在保存客户采购默认配置...");
    }

    /// <summary>提交原生缓冲后恢复已接纳业务基线，不改变已应用筛选。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private void RejectListChanges()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return;
        Lines.RejectChanges();
        BindReferenceSelections();
        SelectedLine = Lines.Items.LastOrDefault();
        UpdateChangeState();
    }

    /// <summary>页码属于最后成功查询的条件，取消及失败不接纳新页码。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !CanQuery()) return;
        if (!await EnsureCanDiscardChangesAsync()) { RestoreFilterEditors(); return; }
        RestoreFilterEditors();
        await LoadPageAsync(Math.Max(1, request.NewIndex), request.NewSize <= 0 ? 20 : request.NewSize, _appliedFilters);
    }

    /// <summary>确认客户选择并把稳定业务编码写入当前行。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void CommitCustomer(CustomerPoConfigLineModel? line)
    {
        if (!CanEditRow(line) || line!.SelectedCustomer is null) return;
        line.CustomerId = line.SelectedCustomer.Uid;
        line.CustomerCode = line.SelectedCustomer.CustomerCode?.Trim() ?? string.Empty;
    }

    /// <summary>显式清空当前行客户。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void ClearCustomer(CustomerPoConfigLineModel? line)
    {
        if (!CanEditRow(line)) return;
        line!.SelectedCustomer = null;
        line.CustomerId = null;
        line.CustomerCode = string.Empty;
    }

    /// <summary>确认供应商选择并把稳定业务编码写入当前行。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void CommitSupplier(CustomerPoConfigLineModel? line)
    {
        if (!CanEditRow(line) || line!.SelectedSupplier is null) return;
        line.SupplierId = line.SelectedSupplier.Uid;
        line.SupplierCode = line.SelectedSupplier.SupplierCode.Trim();
    }

    /// <summary>显式清空当前行供应商。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void ClearSupplier(CustomerPoConfigLineModel? line)
    {
        if (!CanEditRow(line)) return;
        line!.SelectedSupplier = null;
        line.SupplierId = null;
        line.SupplierCode = string.Empty;
    }

    /// <summary>执行可取消的分页查询，仅最新且页面存活的结果可回写。</summary>
    private async Task<bool> LoadPageAsync(int pageIndex, int pageSize, QueryFilters filters)
    {
        if (!CanView) return false;
        var identity = _user.CurrentUser;
        var selectedId = SelectedLine?.Uid;
        var sequence = Interlocked.Increment(ref _querySequence);
        var currentCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _queryCts, currentCts);
        previous?.Cancel();
        // 旧请求自己的 finally 负责释放 CTS。
        IsQuerying = true;
        try
        {
            var request = new CustomerPoConfigSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(filters.Keyword) ? null : filters.Keyword.Trim(),
                CustomerId = filters.Customer?.Uid,
                SupplierId = filters.Supplier?.Uid,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
            var result = await _runner.RunAsync(() => _api.GetPagedAsync(request, currentCts.Token), currentCts.Token, "加载客户采购默认配置失败");
            if (!CanView || !ReferenceEquals(identity, _user.CurrentUser) || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _querySequence)) return false;
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
            PageIndex = page?.PageIndex > 0 ? page.PageIndex : pageIndex;
            PageSize = page?.PageSize > 0 ? page.PageSize : pageSize;
            _appliedFilters = filters; RestoreFilterEditors();
            ResultSummary = $"共 {TotalCount} 条配置，本页 {Lines.Items.Count} 条。";
            AcceptChanges();
            UpdateChangeState();
            return true;
        }
        catch (OperationCanceledException) when (currentCts.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_queryCts, currentCts)) { _queryCts = null; IsQuerying = false; }
            currentCts.Dispose();
        }
    }

    /// <summary>稳定 ID 优先，只有旧记录缺少 ID 时按编码匹配；回显不修改业务资料。</summary>
    private void BindReferenceSelections()
    {
        foreach (var line in Lines.Items)
        {
            line.SelectedCustomer = line.CustomerId is { } customerId ? _cache.Customers.FirstOrDefault(item => item.Uid == customerId)
                : _cache.Customers.FirstOrDefault(item => string.Equals(item.CustomerCode, line.CustomerCode, StringComparison.OrdinalIgnoreCase));
            line.SelectedSupplier = line.SupplierId is { } supplierId ? _cache.Suppliers.FirstOrDefault(item => item.Uid == supplierId)
                : _cache.Suppliers.FirstOrDefault(item => string.Equals(item.SupplierCode, line.SupplierCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>缓存替换后重建候选与行显示，不静默更改业务 ID 或编码。</summary>
    private void OnReferenceCacheChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(CustomerOptions)); OnPropertyChanged(nameof(SupplierOptions));
        BindReferenceSelections();
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

    /// <summary>查询必须有查看许可；关闭可在撤权后确认。等待期间身份变化不能承接旧查询意图。</summary>
    private async Task<bool> EnsureCanDiscardChangesAsync(bool requireViewPermission = true)
    {
        if (_disposed || requireViewPermission && !CanView || IsBusy || IsSelectionPrompt || !TryCommitPendingEdits()) return false;
        if (!HasUnsavedChanges) return true;
        var identity = _user.CurrentUser;
        IsSelectionPrompt = true;
        try { return await _dialogs.ConfirmAsync("放弃未保存变更", "当前配置有未保存修改，继续将放弃这些修改。", "继续")
            && (!requireViewPermission || CanView && ReferenceEquals(identity, _user.CurrentUser)); }
        finally { IsSelectionPrompt = false; }
    }

    private bool TryCommitPendingEdits() => CommitPendingEdits?.Invoke() ?? true;
    private void OnListStateChanged(object? sender, ListStateChangedEventArgs<CustomerPoConfigLineModel> e) => UpdateChangeState();
    /// <summary>交互状态改变时同步可见权限与命令资格，执行入口仍再次复核。</summary>
    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsQuerying) or nameof(IsSelectionPrompt) or nameof(CanWrite))
            NotifyPermissionsAndCommands();
    }

    /// <summary>同步列表草稿与显示行勾选，只有业务字段变化才形成修改。</summary>
    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged();
        RefreshCheckState();
    }

    /// <summary>取消未完成查询并解除事件订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachCheckSelection();
        _user.CurrentUserChanged -= OnPermissionsChanged;
        Lines.ListStateChanged -= OnListStateChanged;
        PropertyChanged -= OnManagerPropertyChanged;
        CollectionChangedEventManager.RemoveHandler(_cache.Currencies, OnCurrenciesChanged);
        CollectionChangedEventManager.RemoveHandler(_cache.Customers, OnReferenceCacheChanged);
        CollectionChangedEventManager.RemoveHandler(_cache.Suppliers, OnReferenceCacheChanged);
        CommitPendingEdits = null;
        var cts = Interlocked.Exchange(ref _queryCts, null);
        cts?.Cancel();
        // 请求 finally 拥有释放权，页面关闭只取消查询。
    }

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => IsInteractionEnabled && CanWrite
        && request is not null && request.Fields.All(field => field is nameof(CustomerPoConfigLineModel.Currency)
            or nameof(CustomerPoConfigLineModel.DefaultDiscountRate)
            or nameof(CustomerPoConfigLineModel.DiscountRemark)
            or nameof(CustomerPoConfigLineModel.InletHoseType)
            or nameof(CustomerPoConfigLineModel.AeratorType)
            or nameof(CustomerPoConfigLineModel.PalletType)
            or nameof(CustomerPoConfigLineModel.BoxType)
            or nameof(CustomerPoConfigLineModel.LabelType)
            or nameof(CustomerPoConfigLineModel.IMType)
            or nameof(CustomerPoConfigLineModel.CartonType)
            or nameof(CustomerPoConfigLineModel.LogoType)
            or nameof(CustomerPoConfigLineModel.SalesPerson)
            or nameof(CustomerPoConfigLineModel.Purchaser)
            or nameof(CustomerPoConfigLineModel.Note))
        && request.TryGetRows<CustomerPoConfigLineModel>(Lines.Items, out _, out var targets)
        && targets.Count > 0 && targets.All(CanEditRow);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<CustomerPoConfigLineModel>(Lines.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(CanEditRow))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(CustomerPoConfigLineModel.Currency): target.Currency = source.Currency; break;
                    case nameof(CustomerPoConfigLineModel.DefaultDiscountRate): target.DefaultDiscountRate = source.DefaultDiscountRate; break;
                    case nameof(CustomerPoConfigLineModel.DiscountRemark): target.DiscountRemark = source.DiscountRemark; break;
                    case nameof(CustomerPoConfigLineModel.InletHoseType): target.InletHoseType = source.InletHoseType; break;
                    case nameof(CustomerPoConfigLineModel.AeratorType): target.AeratorType = source.AeratorType; break;
                    case nameof(CustomerPoConfigLineModel.PalletType): target.PalletType = source.PalletType; break;
                    case nameof(CustomerPoConfigLineModel.BoxType): target.BoxType = source.BoxType; break;
                    case nameof(CustomerPoConfigLineModel.LabelType): target.LabelType = source.LabelType; break;
                    case nameof(CustomerPoConfigLineModel.IMType): target.IMType = source.IMType; break;
                    case nameof(CustomerPoConfigLineModel.CartonType): target.CartonType = source.CartonType; break;
                    case nameof(CustomerPoConfigLineModel.LogoType): target.LogoType = source.LogoType; break;
                    case nameof(CustomerPoConfigLineModel.SalesPerson): target.SalesPerson = source.SalesPerson; break;
                    case nameof(CustomerPoConfigLineModel.Purchaser): target.Purchaser = source.Purchaser; break;
                    case nameof(CustomerPoConfigLineModel.Note): target.Note = source.Note; break;
                }
            }
        }
    }
}

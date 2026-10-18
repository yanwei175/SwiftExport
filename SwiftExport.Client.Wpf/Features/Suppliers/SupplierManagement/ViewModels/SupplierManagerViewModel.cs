using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;

/// <summary>维护供应商全量快照、币种候选与编辑生命周期；列筛选由视图和现有筛选库负责。</summary>
public sealed partial class SupplierManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly ICurrentUserService _user;
    private CancellationTokenSource? _loadCancellation;
    private long _loadVersion;
    private bool _initialized, _disposed;

    /// <summary>注入供应商全量读取、批量保存、会话权限和反馈；不再创建产品抽屉。</summary>
    public SupplierManagerViewModel(ISupplierApi supplierApi, IApiCallRunner apiCallRunner, IMessageService messageService,
        IDialogService dialogService, IReferenceDataCacheService referenceDataCache, ICurrentUserService currentUser)
    {
        _supplierApi = supplierApi; _apiCallRunner = apiCallRunner; _messageService = messageService;
        _dialogService = dialogService; _referenceDataCache = referenceDataCache; _user = currentUser;
        Title = "供应商管理";
        Suppliers.PropertyChanged += OnListPropertyChanged;
        PropertyChanged += OnPageStateChanged;
        _user.CurrentUserChanged += OnPermissionsChanged;
        CollectionChangedEventManager.AddHandler(_referenceDataCache.Currencies, OnCurrenciesChanged);
        RefreshCurrencyOptions();
        InitializeCheckSelection();
        foreach (var command in new IRelayCommand[] { RefreshCommand, AddRowCommand,
                     DeleteSelectedCommand, SaveCommand, RejectCommand, FillDownCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>全量资料及隐藏行草稿共享同一列表，不复制筛选结果。</summary>
    public SupplierListModel Suppliers { get; } = new();
    /// <summary>页面标题参与文档页签。</summary>
    public string DocumentTitle => Title;
    /// <summary>所有加载行的草稿，包括筛选隐藏的行。</summary>
    public bool HasUnsavedChanges => Suppliers.HasChanges;
    /// <summary>保留既有保存状态名称。</summary>
    public bool HasChanges => Suppliers.HasChanges;
    /// <summary>提交原生编辑缓冲，无效输入阻止操作。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>与客户页一致的币种缓存选项，空项只属于页面，不写回共享缓存。</summary>
    [ObservableProperty] public partial IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; set; } = [];
    /// <summary>当前完整行；格子模式不提供行级删除和选区勾选。</summary>
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial SupplierLineModel? SelectedLine { get; set; }
    /// <summary>表格两个入口共享的原生选择模式。</summary>
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsCellSelectionMode { get; set; }
    /// <summary>放弃草稿确认期间禁止其他操作。</summary>
    [ObservableProperty] public partial bool IsSelectionPrompt { get; set; }
    /// <summary>全量查询期间冻结编辑。</summary>
    [ObservableProperty] public partial bool IsQuerying { get; private set; }
    /// <summary>显示数与全量快照数的唯一状态提示。</summary>
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载供应商资料。";

    /// <summary>查看与写入使用现有权限键，不按角色名称兜底。</summary>
    public bool CanView => !_disposed && _user.HasPermission(PermissionKeys.SuppliersSupplierView);
    /// <summary>新增行独立使用创建权限。</summary>
    public bool CanCreate => CanView && _user.HasPermission(PermissionKeys.SuppliersSupplierCreate);
    /// <summary>既有行独立使用编辑权限。</summary>
    public bool CanEdit => CanView && _user.HasPermission(PermissionKeys.SuppliersSupplierEdit);
    /// <summary>既有行独立使用删除权限。</summary>
    public bool CanDeleteRecords => CanView && _user.HasPermission(PermissionKeys.SuppliersSupplierDelete);
    /// <summary>页面在读取、写入和确认期间冻结。</summary>
    public bool IsInteractionEnabled => CanView && IsNotBusy && !IsQuerying && !IsSelectionPrompt;
    /// <summary>无创建及编辑权限时文本只读，仍可查看和勾选。</summary>
    public bool IsGridReadOnly => !CanRun() || !CanWrite || !(CanCreate || CanEdit);
    /// <summary>编辑器和填充复核实际行归属及新增/既有权限。</summary>
    public bool CanEditRow(SupplierLineModel? row) => CanRun() && CanWrite && row is { IsDeleted: false, IsReadOnly: false }
        && Suppliers.Items.Contains(row) && (row.IsNew ? CanCreate : CanEdit);

    partial void OnSelectedLineChanged(SupplierLineModel? value) => Suppliers.SelectedItem = value;

    /// <summary>幂等加载币种缓存和供应商完整快照，不使用服务器分页冒充全量。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || !CanView) return;
        _initialized = true; await LoadAllAsync(false);
    }

    /// <summary>刷新前确认草稿，读取失败仍保留原快照和筛选。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadAllAsync(true);

    /// <summary>复核创建权限后新增，筛选条件不随新增重置。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddRow()
    {
        if (!CanAdd() || !CommitEdits()) return;
        SelectedLine = Suppliers.AddNewSupplier(); UpdateChangeState();
    }

    /// <summary>勾选优先，确认后再核对身份与完整目标权限；持久化删除留在删除池。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDelete() || !CommitEdits()) return;
        var identity = _user.CurrentUser; var rows = DeleteTargets();
        if (rows.Any(row => !row.IsNew) && !await ConfirmAsync("删除供应商", $"确定删除 {rows.Length} 个供应商吗？已保存供应商将在保存时删除，有关联资料时后端可能拒绝。", "删除")) return;
        if (!CanDelete() || !ReferenceEquals(identity, _user.CurrentUser) || !rows.SequenceEqual(DeleteTargets())) return;
        foreach (var row in rows) Suppliers.Remove(row);
        SelectedLine = GetVisibleRows().FirstOrDefault(); UpdateChangeState();
    }

    /// <summary>冻结全量列表的变更集，包括隐藏草稿；发送及接纳前分别复核权限与身份。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !CommitEdits() || !CanSave()) return;
        var identity = _user.CurrentUser;
        await RunWriteAsync(async () =>
        {
            var error = await Suppliers.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error)) { _messageService.ShowWarning("供应商资料未保存", error); return; }
            if (!CanStageChanges() || !ReferenceEquals(identity, _user.CurrentUser)) return;
            var request = Suppliers.CreateSaveRequest(); if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(() => _supplierApi.BatchSaveAsync(request), "保存供应商资料失败");
            if (!CanStageChanges() || !ReferenceEquals(identity, _user.CurrentUser)) return;
            if (!result.Success || result.Data is null) { _messageService.ShowError("保存供应商资料失败", result.Message); return; }
            Suppliers.ApplySaveResponse(result.Data); UpdateChangeState();
            _messageService.ShowSuccess("供应商资料已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            var cache = await _referenceDataCache.RefreshSuppliersAsync();
            if (CanView && !cache.Success) _messageService.ShowWarning("供应商资料已保存，但缓存刷新失败", cache.Message);
        }, "正在保存供应商资料...");
    }

    /// <summary>放弃仅恢复业务基线，不清除列筛选或共享缓存。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Reject()
    {
        if (!CanRun() || !CommitEdits()) return;
        Suppliers.RejectChanges(); SelectedLine = GetVisibleRows().FirstOrDefault(); UpdateChangeState();
    }

    /// <summary>取消及存活序号共同阻止释放、撤权后的快照回写。</summary>
    private async Task LoadAllAsync(bool confirmChanges)
    {
        if (!CanRun() || confirmChanges && !await PrepareReloadAsync()) return;
        if (!CanRun()) return;
        _loadCancellation?.Cancel(); using var cts = new CancellationTokenSource();
        _loadCancellation = cts; var version = ++_loadVersion; var identity = _user.CurrentUser;
        IsQuerying = true;
        try
        {
            var cacheResult = await _referenceDataCache.EnsureLoadedAsync().WaitAsync(cts.Token);
            if (!CanView || cts.IsCancellationRequested || version != _loadVersion || !ReferenceEquals(identity, _user.CurrentUser)) return;
            RefreshCurrencyOptions();
            if (!cacheResult.Success) _messageService.ShowWarning("加载供应商币种失败", cacheResult.Message);
            var result = await _apiCallRunner.RunAsync(() => _supplierApi.GetAllAsync(cts.Token), cts.Token, "加载供应商资料失败");
            if (!CanView || cts.IsCancellationRequested || version != _loadVersion || !ReferenceEquals(identity, _user.CurrentUser)) return;
            if (!result.Success) { _messageService.ShowError("加载供应商资料失败", result.Message); return; }
            Suppliers.LoadFromDtos(result.Data ?? []); SelectedLine = GetVisibleRows().FirstOrDefault(); UpdateChangeState();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_loadCancellation, cts)) { _loadCancellation = null; if (!_disposed) IsQuerying = false; } }
    }

    /// <summary>关闭只保护本页草稿，撤权不会使页面无法关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => PrepareReloadAsync(cancellationToken);
    /// <summary>刷新和关闭共用输入及草稿门禁；关闭不依赖当前查看权限。</summary>
    private async Task<bool> PrepareReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy || IsSelectionPrompt || !CommitEdits()) return false;
        return !HasUnsavedChanges || await ConfirmAsync("放弃供应商资料修改", "当前供应商列表有未保存修改，继续将放弃这些修改。", "继续", cancellationToken);
    }

    /// <summary>确认状态只有一个 owner，避免删除与刷新同时改变草稿。</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string button, CancellationToken token = default)
    {
        if (IsSelectionPrompt || _disposed || IsBusy) return false;
        IsSelectionPrompt = true;
        try { return await _dialogService.ConfirmAsync(title, message, button, cancellationToken: token) && !_disposed; }
        finally { if (!_disposed) IsSelectionPrompt = false; }
    }

    private bool CommitEdits() => CommitPendingEdits?.Invoke() ?? true;
    private bool CanRun() => IsInteractionEnabled;
    private bool CanAdd() => CanRun() && CanWrite && CanCreate;
    private SupplierLineModel[] DeleteTargets() => Suppliers.CheckedCount > 0 ? Suppliers.CheckedItems.ToArray() : SelectedLine is null ? [] : [SelectedLine];
    private bool CanDelete() => !IsCellSelectionMode && CanRun() && CanWrite && DeleteTargets() is { Length: > 0 } rows
        && rows.All(row => !row.IsDeleted && Suppliers.Items.Contains(row) && (row.IsNew ? CanCreate : CanDeleteRecords));
    private bool CanSave() => CanRun() && CanWrite && (CanCreate || CanEdit || CanDeleteRecords) && CanStageChanges();
    /// <summary>Busy 内按变更类型复核，不能把当前写入自身的忙碌当作撤权。</summary>
    private bool CanStageChanges() => CanView && CanWrite && (Suppliers.AddedItems.Count == 0 || CanCreate)
        && (Suppliers.ModifiedItems.Count == 0 || CanEdit) && (Suppliers.DeletedItems.Count == 0 || CanDeleteRecords);

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();
    /// <summary>共享缓存刷新只更新选项；不改写供应商草稿中已经选择的币种代码。</summary>
    private void OnCurrenciesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_disposed) RefreshCurrencyOptions();
    }

    /// <summary>消费现有只读币种契约，沿用客户页的空值语义及缓存顺序，不修改缓存对象。</summary>
    private void RefreshCurrencyOptions()
    {
        CurrencyOptions = new[]
        {
            new SysDicCurrencyDto
            {
                Uid = Guid.Empty, CurrencyCode = string.Empty, CurrencyNameZh = "（空）",
                CurrencyNameEn = string.Empty, CurrencySymbol = string.Empty, IsEnabled = true
            }
        }.Concat(_referenceDataCache.Currencies).ToArray();
    }

    /// <summary>筛选只更新显示聚合，不将隐藏行移出业务列表。</summary>
    private void UpdateChangeState()
    {
        IsDirty = HasChanges; OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(HasUnsavedChanges)); RefreshCheckState();
        ResultSummary = $"显示 {GetVisibleRows().Length} 条 / 共 {Suppliers.Items.Count} 条。";
        SaveCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged();
    }
    /// <summary>读取、确认和写入状态统一刷新命令与表格能力。</summary>
    private void OnPageStateChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(IsBusy) or nameof(IsQuerying) or nameof(IsSelectionPrompt) or nameof(CanWrite)) NotifyCapabilities(); }
    /// <summary>运行时权限变化取消读取；保持已有草稿，更新文本编辑和所有入口。</summary>
    private void OnPermissionsChanged(object? sender, EventArgs e) { ++_loadVersion; _loadCancellation?.Cancel(); NotifyCapabilities(); }
    /// <summary>权限或运行状态变化后刷新所有页面入口，不改变现有草稿。</summary>
    private void NotifyCapabilities()
    {
        OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(IsGridReadOnly));
        OnPropertyChanged(nameof(IsInteractionEnabled));
        foreach (var command in new IRelayCommand[] { RefreshCommand, AddRowCommand, DeleteSelectedCommand, SaveCommand, RejectCommand, FillDownCommand }) command.NotifyCanExecuteChanged();
        RefreshCheckState();
    }

    /// <summary>释放所有本页订阅及回调，不影响独立供应商产品页或共享缓存。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_loadVersion; _loadCancellation?.Cancel();
        Suppliers.PropertyChanged -= OnListPropertyChanged; PropertyChanged -= OnPageStateChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
        CollectionChangedEventManager.RemoveHandler(_referenceDataCache.Currencies, OnCurrenciesChanged);
        DetachCheckSelection(); CommitPendingEdits = null;
    }

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => CanRun() && CanWrite
        && request is not null && request.Fields.All(field => field is nameof(SupplierLineModel.SupplierName)
            or nameof(SupplierLineModel.DocRelateNo)
            or nameof(SupplierLineModel.ContactPerson)
            or nameof(SupplierLineModel.ContactPhone)
            or nameof(SupplierLineModel.ContactEmail)
            or nameof(SupplierLineModel.Address)
            or nameof(SupplierLineModel.Discount)
            or nameof(SupplierLineModel.Currency)
            or nameof(SupplierLineModel.TaxRate)
            or nameof(SupplierLineModel.DefaultLeadTimeDays)
            or nameof(SupplierLineModel.PaymentTerms)
            or nameof(SupplierLineModel.DiscountRemark))
        && request.TryGetRows<SupplierLineModel>(Suppliers.Items, out _, out var targets)
        && targets.Count > 0 && targets.All(CanEditRow);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<SupplierLineModel>(Suppliers.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(CanEditRow))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(SupplierLineModel.SupplierName): target.SupplierName = source.SupplierName; break;
                    case nameof(SupplierLineModel.DocRelateNo): target.DocRelateNo = source.DocRelateNo; break;
                    case nameof(SupplierLineModel.ContactPerson): target.ContactPerson = source.ContactPerson; break;
                    case nameof(SupplierLineModel.ContactPhone): target.ContactPhone = source.ContactPhone; break;
                    case nameof(SupplierLineModel.ContactEmail): target.ContactEmail = source.ContactEmail; break;
                    case nameof(SupplierLineModel.Address): target.Address = source.Address; break;
                    case nameof(SupplierLineModel.Discount): target.Discount = source.Discount; break;
                    case nameof(SupplierLineModel.Currency): target.Currency = source.Currency; break;
                    case nameof(SupplierLineModel.TaxRate): target.TaxRate = source.TaxRate; break;
                    case nameof(SupplierLineModel.DefaultLeadTimeDays): target.DefaultLeadTimeDays = source.DefaultLeadTimeDays; break;
                    case nameof(SupplierLineModel.PaymentTerms): target.PaymentTerms = source.PaymentTerms; break;
                    case nameof(SupplierLineModel.DiscountRemark): target.DiscountRemark = source.DiscountRemark; break;
                }
            }
        }
    }
}

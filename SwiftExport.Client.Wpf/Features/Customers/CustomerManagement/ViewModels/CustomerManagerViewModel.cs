using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;

/// <summary>维护客户全量资料及筛选结果勾选；业务写入仍由原有保存命令负责。</summary>
public sealed partial class CustomerManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;
    private int _loadVersion;
    private readonly ICustomerApi _customerApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private bool _initialized;

    /// <summary>注入客户读取与写入入口、共享字典；订阅在释放时解除。</summary>
    public CustomerManagerViewModel(
        ICustomerApi customerApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache)
    {
        _customerApi = customerApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        Title = "客户管理";
        Customers.PropertyChanged += OnListPropertyChanged;
        CollectionChangedEventManager.AddHandler(CollectionViewSource.GetDefaultView(Customers.Items), OnVisibleCustomersChanged);
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.DictionaryValues,
            OnDictionaryValuesChanged);
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.Currencies,
            OnCurrenciesChanged);
        RefreshDictionaryOptions();
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(SetVisibleChecksCommand);
        RegisterBusyAwareCommand(CheckSelectionCommand);
        RegisterBusyAwareCommand(UncheckSelectionCommand);
        RegisterBusyAwareCommand(InvertSelectionCommand);
        RegisterBusyAwareCommand(CheckVisibleCommand);
        RegisterBusyAwareCommand(InvertVisibleCommand);
    }

    public CustomerListModel Customers { get; } = new();
    public bool HasChanges => Customers.HasChanges;
    /// <summary>使用当前页面标题参与导航页签呈现。</summary>
    public string DocumentTitle => Title;
    /// <summary>客户草稿独立参与关闭保护，客户产品由独立工作区维护。</summary>
    public bool HasUnsavedChanges => Customers.HasChanges;

    /// <summary>表头全选仅反映当前筛选结果，不包含隐藏客户。</summary>
    public bool IsAllVisibleChecked => GetVisibleCustomers() is { Length: > 0 } rows && rows.All(row => row.IsChecked);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerLineModel? SelectedLine { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UncheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    public partial bool IsCellSelectionMode { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载客户资料。";
    [ObservableProperty] public partial IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> PaymentMethodOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> ShippedByOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermLocationOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermSortRemarkOptions { get; set; } = [];

    /// <summary>幂等加载字典和完整客户快照，释放后的延续不再更新页面。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        if (_disposed) return;
        RefreshDictionaryOptions();
        if (!cacheResult.Success)
        {
            _messageService.ShowError("加载客户字典失败", cacheResult.Message);
        }
        await LoadAllAsync(false);
    }

    partial void OnSelectedLineChanged(CustomerLineModel? value) => Customers.SelectedItem = value;

    /// <summary>刷新前确认客户的未保存修改，仅成功读取后接纳新快照。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadAllAsync(true);

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        if (!CanWriteCommand()) return;
        SelectedLine = Customers.AddNewCustomer();
        UpdateChangeState();
    }

    /// <summary>勾选菜单选区涉及的当前客户行，不改变原生选区或业务字段。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void CheckSelection(IList? rows) => SetSelectedChecks(rows, true);

    /// <summary>取消菜单选区内客户行的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void UncheckSelection(IList? rows) => SetSelectedChecks(rows, false);

    /// <summary>逐行反转整行选区的勾选状态。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void InvertSelection(IList? rows) => SetSelectedChecks(rows, null);

    /// <summary>勾选全部已加载客户的筛选结果，不影响隐藏行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void CheckVisible() => SetCheckedSelection(GetVisibleCustomers(), true);

    /// <summary>逐行反转全部已加载客户筛选结果的勾选，保留隐藏行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void InvertVisible() => SetCheckedSelection(GetVisibleCustomers(), null);

    /// <summary>表头复用筛选结果批量勾选，隐藏行保持原状。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void SetVisibleChecks(bool? value) => SetCheckedSelection(GetVisibleCustomers(), value == true);

    /// <summary>以表格使用的默认集合视图限定范围，排除已删除行和占位符。</summary>
    private CustomerLineModel[] GetVisibleCustomers() => CollectionViewSource.GetDefaultView(Customers.Items)
        .Cast<object>().OfType<CustomerLineModel>().Where(row => !row.IsDeleted).ToArray();

    private bool CanChangeVisibleChecks() => CanRun() && GetVisibleCustomers().Length > 0;

    /// <summary>仅在整行模式允许选区命令；空选区由执行入口直接忽略。</summary>
    private bool CanChangeCheckedSelection() => CanRun() && !IsCellSelectionMode;

    /// <summary>读取原生选区集合的当前内容，不保留控件集合，也不接受格选区或非客户对象。</summary>
    private void SetSelectedChecks(IList? selection, bool? value)
    {
        if (!CanChangeCheckedSelection() || selection is not { Count: > 0 }) return;
        var rows = selection.OfType<CustomerLineModel>().ToArray();
        if (rows.Length != selection.Count) return;
        SetCheckedSelection(rows, value);
    }

    /// <summary>只改变 UI 勾选状态并复用列表通知合并；不抑制模型通知、不制造业务脏状态。</summary>
    private void SetCheckedSelection(IReadOnlyList<CustomerLineModel>? rows, bool? value)
    {
        if (!CanRun() || rows is not { Count: > 0 }) return;
        var visible = GetVisibleCustomers().ToHashSet();
        if (!rows.All(visible.Contains)) return;
        using (Customers.SuppressListEvents())
        {
            foreach (var row in rows!.Distinct()) row.IsChecked = value ?? !row.IsChecked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var lines = Customers.CheckedCount > 0
            ? Customers.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (lines.Length == 0) return;
        var persistedCount = lines.Count(line => !line.IsNew);
        if (persistedCount > 0 && !await _dialogService.ConfirmAsync(
                "删除客户",
                $"确定删除 {lines.Length} 个客户吗？其中 {persistedCount} 个已保存客户将在保存时删除；有关联客户产品的客户会被后端阻止。",
                "删除")) return;
        foreach (var line in lines) Customers.Remove(line);
        SelectedLine = GetVisibleCustomers().FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var error = await Customers.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("客户资料未保存", error);
                return;
            }
            var request = Customers.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(() => _customerApi.BatchSaveAsync(request), "保存客户资料失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存客户资料失败", result.Message);
                return;
            }
            Customers.ApplySaveResponse(result.Data);
            SelectedLine = GetVisibleCustomers().FirstOrDefault(line => ReferenceEquals(line, SelectedLine)) ?? GetVisibleCustomers().FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("客户资料已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            var cacheResult = await _referenceDataCache.RefreshCustomersAsync();
            if (!cacheResult.Success)
            {
                _messageService.ShowWarning("客户资料已保存，但缓存刷新失败", cacheResult.Message);
            }
        }, "正在保存客户资料...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Customers.RejectChanges();
        SelectedLine = GetVisibleCustomers().FirstOrDefault();
        UpdateChangeState();
    }

    /// <summary>读取全量快照；取消及释放后不回写，失败保留原模型和草稿。</summary>
    private async Task LoadAllAsync(bool confirmChanges)
    {
        if (_disposed || IsBusy) return;
        if (confirmChanges && !await PrepareReloadAsync()) return;
        if (_disposed || IsBusy) return;
        _loadCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var version = ++_loadVersion;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(() => _customerApi.GetAllAsync(cancellation.Token),
                    cancellation.Token, "加载客户资料失败");
                if (_disposed || cancellation.IsCancellationRequested || version != _loadVersion) return;
                if (!result.Success)
                {
                    _messageService.ShowError("加载客户资料失败", result.Message);
                    return;
                }
                Customers.LoadFromDtos(result.Data ?? []);
                SelectedLine = GetVisibleCustomers().FirstOrDefault();
                AcceptChanges();
                UpdateChangeState();
            }, "正在加载客户资料...");
        }
        finally { if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null; }
    }

    private async Task<bool> PrepareReloadAsync()
    {
        if (
            Customers.HasChanges && !await _dialogService.ConfirmAsync
            (
                "放弃客户资料修改",
                "当前客户列表有未保存修改，继续将放弃这些修改。",
                "继续"
                )
            )
        {

            return false;
        }
        return true;
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateChangeState();
        RefreshVisibleState();
    }

    /// <summary>筛选和列表变化只刷新统计与表头状态，不复制行或清除隐藏行勾选。</summary>
    private void OnVisibleCustomersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshVisibleState();

    private void RefreshVisibleState()
    {
        ResultSummary = $"显示 {GetVisibleCustomers().Length} 条 / 共 {Customers.Items.Count} 条。";
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        CheckVisibleCommand.NotifyCanExecuteChanged();
        InvertVisibleCommand.NotifyCanExecuteChanged();
        SetVisibleChecksCommand.NotifyCanExecuteChanged();
    }

    private void OnDictionaryValuesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshDictionaryOptions();

    private void OnCurrenciesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshCurrencyOptions();

    private void RefreshDictionaryOptions()
    {
        RefreshCurrencyOptions();
        PaymentMethodOptions = CreateDictionaryOptions(DictCodes.PaymentMethod);
        IncotermOptions = CreateDictionaryOptions(DictCodes.Incoterm);
        ShippedByOptions = CreateDictionaryOptions(DictCodes.ShippedBy);
        IncotermLocationOptions = CreateDictionaryOptions(DictCodes.IncotermLocation);
        IncotermSortRemarkOptions = CreateDictionaryOptions(DictCodes.IncotermSortRemark);
    }

    private void RefreshCurrencyOptions()
    {
        // 空选项只属于当前页面视图，不写回应用级缓存。
        var currencies = _referenceDataCache.Currencies;
        var options = new SysDicCurrencyDto[currencies.Count + 1];
        options[0] = new SysDicCurrencyDto
        {
            Uid = Guid.Empty,
            CurrencyCode = string.Empty,
            CurrencyNameZh = "（空）",
            CurrencyNameEn = string.Empty,
            CurrencySymbol = string.Empty,
            IsEnabled = true,
        };

        for (var index = 0; index < currencies.Count; index++)
        {
            options[index + 1] = currencies[index];
        }

        CurrencyOptions = options;
    }

    private IReadOnlyList<SysDictDataDto> CreateDictionaryOptions(string dictCode)
    {
        var cachedValues = _referenceDataCache.GetDictionaryValues(dictCode);
        var options = new SysDictDataDto[cachedValues.Count + 1];
        options[0] = new SysDictDataDto
        {
            Uid = Guid.Empty,
            DictCode = dictCode,
            DataNameEn = string.Empty,
            DataNameZh = "（空）",
            Remark = string.Empty,
            CssClass = string.Empty,
            IsEnabled = true,
        };

        for (var index = 0; index < cachedValues.Count; index++)
        {
            options[index + 1] = cachedValues[index];
        }

        return options;
    }

    private void UpdateChangeState()
    {
        IsDirty = Customers.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => !_disposed && IsNotBusy;
    private bool CanWriteCommand() => IsNotBusy && CanWrite;
    private bool CanDelete() => IsNotBusy && CanWrite && (Customers.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsNotBusy && CanWrite && Customers.HasChanges;
    private bool CanReject() => IsNotBusy && Customers.HasChanges;

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => CanWriteCommand()
        && request is not null && request.Fields.All(field => field is nameof(CustomerLineModel.CustomerName)
            or nameof(CustomerLineModel.DocRelateNo)
            or nameof(CustomerLineModel.ContactPerson)
            or nameof(CustomerLineModel.PhoneNumber)
            or nameof(CustomerLineModel.Address)
            or nameof(CustomerLineModel.Incoterm)
            or nameof(CustomerLineModel.IncotermLocation)
            or nameof(CustomerLineModel.IncotermSortRemark)
            or nameof(CustomerLineModel.DepositRate)
            or nameof(CustomerLineModel.LeadTimeDays)
            or nameof(CustomerLineModel.PaymentMethod)
            or nameof(CustomerLineModel.Currency)
            or nameof(CustomerLineModel.ShippedBy)
            or nameof(CustomerLineModel.EtdSortRemark))
        && request.TryGetRows<CustomerLineModel>(Customers.Items, out _, out var targets)
        && targets.Any(row => !row.IsDeleted && !row.IsReadOnly);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<CustomerLineModel>(Customers.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(row => !row.IsDeleted && !row.IsReadOnly))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(CustomerLineModel.CustomerName): target.CustomerName = source.CustomerName; break;
                    case nameof(CustomerLineModel.DocRelateNo): target.DocRelateNo = source.DocRelateNo; break;
                    case nameof(CustomerLineModel.ContactPerson): target.ContactPerson = source.ContactPerson; break;
                    case nameof(CustomerLineModel.PhoneNumber): target.PhoneNumber = source.PhoneNumber; break;
                    case nameof(CustomerLineModel.Address): target.Address = source.Address; break;
                    case nameof(CustomerLineModel.Incoterm): target.Incoterm = source.Incoterm; break;
                    case nameof(CustomerLineModel.IncotermLocation): target.IncotermLocation = source.IncotermLocation; break;
                    case nameof(CustomerLineModel.IncotermSortRemark): target.IncotermSortRemark = source.IncotermSortRemark; break;
                    case nameof(CustomerLineModel.DepositRate): target.DepositRate = source.DepositRate; break;
                    case nameof(CustomerLineModel.LeadTimeDays): target.LeadTimeDays = source.LeadTimeDays; break;
                    case nameof(CustomerLineModel.PaymentMethod): target.PaymentMethod = source.PaymentMethod; break;
                    case nameof(CustomerLineModel.Currency): target.Currency = source.Currency; break;
                    case nameof(CustomerLineModel.ShippedBy): target.ShippedBy = source.ShippedBy; break;
                    case nameof(CustomerLineModel.EtdSortRemark): target.EtdSortRemark = source.EtdSortRemark; break;
                }
            }
        }
    }
    /// <summary>关闭前保留客户草稿确认；正在写入或读取时不关闭页面。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return !IsBusy && await PrepareReloadAsync();
    }

    /// <summary>页面关闭时取消读取并解除自有订阅，不清空共享字典缓存。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_loadVersion;
        _loadCancellation?.Cancel();
        Customers.PropertyChanged -= OnListPropertyChanged;
        CollectionChangedEventManager.RemoveHandler(CollectionViewSource.GetDefaultView(Customers.Items), OnVisibleCustomersChanged);
        CollectionChangedEventManager.RemoveHandler(_referenceDataCache.DictionaryValues, OnDictionaryValuesChanged);
        CollectionChangedEventManager.RemoveHandler(_referenceDataCache.Currencies, OnCurrenciesChanged);
    }

}

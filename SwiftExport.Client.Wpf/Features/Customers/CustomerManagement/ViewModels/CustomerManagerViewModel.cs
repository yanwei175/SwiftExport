using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;

/// <summary>维护客户资料、当前页勾选与客户产品入口；业务写入仍由原有保存命令负责。</summary>
public sealed partial class CustomerManagerViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private readonly ICustomerApi _customerApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private bool _initialized;
    private bool _restoringSelection;

    public CustomerManagerViewModel(
        ICustomerApi customerApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache,
        CustomerProductManagerViewModel customerProducts)
    {
        _customerApi = customerApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        CustomerProducts = customerProducts;
        Title = "客户管理";
        Customers.PropertyChanged += OnListPropertyChanged;
        CustomerProducts.SelectionRestoreRequested += (_, customer) => RestoreSelection(customer);
        CustomerProducts.PropertyChanged += OnCustomerProductsPropertyChanged;
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.DictionaryValues,
            OnDictionaryValuesChanged);
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.Currencies,
            OnCurrenciesChanged);
        RefreshDictionaryOptions();
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(ToggleCustomerProductsCommand);
        RegisterBusyAwareCommand(CheckSelectionCommand);
        RegisterBusyAwareCommand(UncheckSelectionCommand);
        RegisterBusyAwareCommand(InvertSelectionCommand);
        RegisterBusyAwareCommand(CheckVisibleCommand);
        RegisterBusyAwareCommand(UncheckVisibleCommand);
    }

    public CustomerListModel Customers { get; } = new();
    public CustomerProductManagerViewModel CustomerProducts { get; }
    public bool HasChanges => Customers.HasChanges;
    public bool CanChangeSelectionMode => !CustomerProducts.IsOpen;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerLineModel? SelectedLine { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCustomerProductsCommand))]
    public partial bool IsCellSelectionMode { get; set; }
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
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载客户资料。";
    [ObservableProperty] public partial IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> PaymentMethodOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> ShippedByOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermLocationOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermSortRemarkOptions { get; set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        RefreshDictionaryOptions();
        if (!cacheResult.Success)
        {
            _messageService.ShowError("加载客户字典失败", cacheResult.Message);
        }
        await LoadPageAsync(false);
    }

    partial void OnSelectedLineChanged(CustomerLineModel? value)
    {
        Customers.SelectedItem = value;
        if (!_restoringSelection) CustomerProducts.NotifyCustomerSelection(value);
    }

    partial void OnIsCellSelectionModeChanged(bool value)
    {
        if (value && CustomerProducts.IsOpen)
        {
            IsCellSelectionMode = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleCustomerProducts))]
    private Task ToggleCustomerProductsAsync() =>
        CustomerProducts.ToggleForCustomerAsync(SelectedLine);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null) Keyword = query;
        if (!await PrepareReloadAsync()) return;
        PageIndex = 1;
        await LoadPageAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadPageAsync(true);

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        SelectedLine = Customers.AddNewCustomer();
        UpdateChangeState();
    }

    /// <summary>勾选菜单选区涉及的当前客户行，不改变原生选区或业务字段。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void CheckSelection(IReadOnlyList<CustomerLineModel>? rows) => SetCheckedSelection(rows, true);

    /// <summary>取消菜单选区内客户行的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void UncheckSelection(IReadOnlyList<CustomerLineModel>? rows) => SetCheckedSelection(rows, false);

    /// <summary>逐行反转选区勾选；同一行涉及多个单元格时仅反转一次。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void InvertSelection(IReadOnlyList<CustomerLineModel>? rows) => SetCheckedSelection(rows, null);

    /// <summary>勾选当前已加载页的筛选结果，不加载其他页、不影响隐藏行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void CheckVisible() => SetCheckedSelection(GetVisibleCustomers(), true);

    /// <summary>清空当前已加载页筛选结果的勾选，保留隐藏行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void UncheckVisible() => SetCheckedSelection(GetVisibleCustomers(), false);

    /// <summary>以表格使用的默认集合视图限定范围，排除已删除行和占位符。</summary>
    private CustomerLineModel[] GetVisibleCustomers() => CollectionViewSource.GetDefaultView(Customers.Items)
        .Cast<object>().OfType<CustomerLineModel>().Where(row => !row.IsDeleted).ToArray();

    private bool CanChangeVisibleChecks() => CanRun() && GetVisibleCustomers().Length > 0;

    /// <summary>执行时重新检查范围，拒绝空选区、换页前快照及已隐藏的行，不回退到当前行。</summary>
    private bool CanChangeCheckedSelection(IReadOnlyList<CustomerLineModel>? rows)
    {
        if (!CanRun() || rows is not { Count: > 0 }) return false;
        var visible = GetVisibleCustomers().ToHashSet();
        return rows.All(visible.Contains);
    }

    /// <summary>只改变 UI 勾选状态并复用列表通知合并；不抑制模型通知、不制造业务脏状态。</summary>
    private void SetCheckedSelection(IReadOnlyList<CustomerLineModel>? rows, bool? value)
    {
        if (!CanChangeCheckedSelection(rows)) return;
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
        SelectedLine = Customers.Items.FirstOrDefault();
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
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Customers.Items.FirstOrDefault(line => ReferenceEquals(line, SelectedLine)) ?? Customers.Items.FirstOrDefault();
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
        SelectedLine = Customers.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        return ChangePageAsync(request.NewIndex, request.NewSize);
    }

    private async Task ChangePageAsync(int index, int size)
    {
        if (!await PrepareReloadAsync()) return;
        PageIndex = Math.Max(1, index);
        PageSize = size <= 0 ? DefaultPageSize : size;
        await LoadPageAsync(false);
    }

    private async Task<bool> LoadPageAsync(bool confirmChanges)
    {
        if (confirmChanges && !await PrepareReloadAsync()) return false;
        var loaded = false;
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(() => _customerApi.GetPagedAsync(new CustomerSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize,
            }), "加载客户资料失败");
            if (!result.Success)
            {
                ResultSummary = "客户资料加载失败。";
                _messageService.ShowError("加载客户资料失败", result.Message);
                return;
            }
            Customers.LoadFromDtos(result.Data?.Items ?? []);
            SelectedLine = Customers.Items.FirstOrDefault();
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 条，本页 {Customers.Items.Count} 条。";
            AcceptChanges();
            UpdateChangeState();
            loaded = true;
        }, "正在加载客户资料...");
        return loaded;
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
        return await CustomerProducts.CloseForNavigationAsync();
    }

    private void RestoreSelection(CustomerLineModel? customer)
    {
        _restoringSelection = true;
        try { SelectedLine = customer; }
        finally { _restoringSelection = false; }
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    private void OnCustomerProductsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(CustomerProductManagerViewModel.IsOpen))
        {
            if (CustomerProducts.IsOpen && IsCellSelectionMode)
            {
                IsCellSelectionMode = false;
            }

            OnPropertyChanged(nameof(CanChangeSelectionMode));
            ToggleCustomerProductsCommand.NotifyCanExecuteChanged();
        }
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
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanToggleCustomerProducts() => CustomerProducts.IsOpen || !IsCellSelectionMode;
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
}

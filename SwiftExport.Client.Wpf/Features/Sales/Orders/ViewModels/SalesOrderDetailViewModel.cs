using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using SwiftExportClient.Authentication;

namespace SwiftExportClient.Features.Sales.Orders.ViewModels;

/// <summary>
/// 销售订单 Detail 页面模型，负责详情编辑、Lookup 和生命周期命令。
/// </summary>
public sealed partial class SalesOrderDetailViewModel : ViewModelBaseEx,
    IDocumentTabViewModel,
    IDocumentTabInitializable
{
    private readonly ISalesOrderDetailApi _detailApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISalesOrderCancellationDialogService _cancellationDialogService;
    private readonly IMessageService _messageService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _productLookupCancellation;
    private CancellationTokenSource? _customerLookupCancellation;
    private bool _loadingDocument;
    private ConfirmSalesOrderCommand? _pendingConfirmation;
    private SaveSalesOrderRequest? _pendingSave;
    private CancelSalesOrderConfirmationCommand? _pendingCancellation;
    private Guid? _refreshOnlyUid;
    private bool _saveOutcomeUnknown;
    private bool _cancellationOutcomeUnknown;
    // 只有此前执行结果未定，前置拒绝才不能结束原意图。
    private bool _confirmationOutcomeUnknown;

    /// <summary>注入 Detail API 以及页面导航、对话框和消息依赖。</summary>
    public SalesOrderDetailViewModel(
        ISalesOrderDetailApi api,
        IApiCallRunner apiCallRunner,
        INavigationService navigationService,
        IDialogService dialogService,
        ISalesOrderCancellationDialogService cancellationDialogService,
        IMessageService messageService,
        IReferenceDataCacheService referenceDataCache,
        ICurrentUserService currentUser)
    {
        _detailApi = api;
        _apiCallRunner = apiCallRunner;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _cancellationDialogService = cancellationDialogService;
        _messageService = messageService;
        _referenceDataCache = referenceDataCache;
        _currentUser = currentUser;
        Title = "销售订单-新建";
        Header.PropertyChanged += OnHeaderPropertyChanged;
        Lines.ListStateChanged += OnLinesStateChanged;
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.DictionaryValues,
            OnDictionaryValuesChanged);
        CollectionChangedEventManager.AddHandler(
            _referenceDataCache.Currencies,
            OnCurrenciesChanged);
        RefreshReferenceOptions();

        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ConfirmCommand);
        RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(RetryCurrentFactCommand);
        RegisterBusyAwareCommand(AddStandardLineCommand);
        RegisterBusyAwareCommand(AddNonStandardLineCommand);
        RegisterBusyAwareCommand(DeleteSelectedLineCommand);
        RegisterBusyAwareCommand(MoveLineUpCommand);
        RegisterBusyAwareCommand(MoveLineDownCommand);
    }

    public SalesOrderHeaderEditModel Header { get; } = new();
    public SalesOrderLineListModel Lines { get; } = new();
    public ObservableCollection<SalesOrderProductLookupDto> ProductCandidates { get; } = [];
    public ObservableCollection<SalesOrderCustomerLookupDto> CustomerCandidates { get; } = [];
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.OrderNo) ? "销售订单-新建" : Header.OrderNo;
    public bool HasUnsavedChanges => IsDirty || HasPendingIntent;
    /// <summary>任何未决写意图或已成功但待刷新状态均冻结编辑与页签关闭。</summary>
    public bool HasPendingIntent => _pendingSave is not null || _pendingConfirmation is not null
        || _pendingCancellation is not null || _refreshOnlyUid.HasValue;
    /// <summary>页面写操作使用独立权限；模块权限仅管理导航。</summary>
    public bool CanMaintain => _currentUser.HasPermission(PermissionKeys.SalesOrderMaintain);
    /// <summary>确认权限不由销售模块访问自动授予。</summary>
    public bool CanConfirmOrder => _currentUser.HasPermission(PermissionKeys.SalesOrderConfirm);
    /// <summary>取消确认权限与正向确认相互独立。</summary>
    public bool CanCancelOrderConfirmation => _currentUser.HasPermission(PermissionKeys.SalesOrderCancelConfirmation);
    public bool IsPersisted => Header.Uid != Guid.Empty;
    /// <summary>单头和附加表单随当前单据状态及未决确认统一冻结。</summary>
    public bool IsEditable => !IsReadOnly;
    public decimal TotalQty => Lines.Items.Sum(line => line.Qty);
    public decimal TotalCnyAmount => Lines.Items.Sum(line => line.CnyAmount ?? 0m);
    public decimal TotalUsdAmount => Lines.Items.Sum(line => line.UsdAmount ?? 0m);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLineUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLineDownCommand))]
    public partial SalesOrderLineModel? SelectedLine { get; set; }

    [ObservableProperty] public partial SalesOrderProductLookupDto? SelectedProductCandidate { get; set; }
    [ObservableProperty] public partial SalesOrderCustomerLookupDto? SelectedCustomerCandidate { get; set; }
    [ObservableProperty] public partial bool IsProductLookupBusy { get; set; }
    [ObservableProperty] public partial bool IsCustomerLookupBusy { get; set; }
    [ObservableProperty] public partial bool HasValidationError { get; set; }
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> PaymentMethodOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> IncotermOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> ShippedByOptions { get; set; } = [];
    /// <summary>非标销售行可选用的单位字典代码。</summary>
    [ObservableProperty] public partial IReadOnlyList<SysDictDataDto> UnitOptions { get; set; } = [];

    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        RefreshReferenceOptions();
        if (!cacheResult.Success)
        {
            _messageService.ShowError("加载销售订单基础资料失败", cacheResult.Message);
        }

        var request = parameter as SalesOrderDocumentOpenRequest;
        if (request?.OrderId is not Guid orderId || orderId == Guid.Empty)
        {
            InitializeNewDocument();
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.GetAsync(orderId, cancellationToken),
                cancellationToken,
                "加载销售订单失败");
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("加载销售订单失败", result.Message);
                return;
            }

            LoadDocument(result.Data);
        }, "正在加载销售订单...");
    }

    private void OnDictionaryValuesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshDictionaryOptions();

    private void OnCurrenciesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshCurrencyOptions();

    private void RefreshReferenceOptions()
    {
        RefreshCurrencyOptions();
        RefreshDictionaryOptions();
    }

    private void RefreshCurrencyOptions()
    {
        // 保留显式空选项，同时让实际币种始终来自独立主数据缓存。
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

    private void RefreshDictionaryOptions()
    {
        PaymentMethodOptions = CreateDictionaryOptions(DictCodes.PaymentMethod);
        IncotermOptions = CreateDictionaryOptions(DictCodes.Incoterm);
        ShippedByOptions = CreateDictionaryOptions(DictCodes.ShippedBy);
        UnitOptions = CreateDictionaryOptions(DictCodes.Unit);
    }

    private IReadOnlyList<SysDictDataDto> CreateDictionaryOptions(string dictCode)
    {
        // 页面选项是缓存快照的轻量包装，禁止修改缓存中的 DTO。
        var values = _referenceDataCache.GetDictionaryValues(dictCode);
        var options = new SysDictDataDto[values.Count + 1];
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
        for (var index = 0; index < values.Count; index++)
        {
            options[index + 1] = values[index];
        }

        return options;
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        // 未知结果仍可能已经提交；页签关闭会丢失唯一可安全重试的命令身份。
        if (IsBusy || HasPendingIntent) return false;
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "关闭销售订单",
            "当前订单有未保存修改。确定放弃修改并关闭吗？",
            "放弃并关闭",
            "继续编辑",
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddStandardLine()
    {
        using (Lines.SuppressListEvents())
        {
            SelectedLine = Lines.AddStandardLine(Header.DeliveryDate);
            ResetLineNumbers();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddNonStandardLine()
    {
        using (Lines.SuppressListEvents())
        {
            SelectedLine = Lines.AddNonStandardLine(Header.DeliveryDate);
            ResetLineNumbers();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteLine))]
    private void DeleteSelectedLine()
    {
        using (Lines.SuppressListEvents())
        {
            Lines.Remove(SelectedLine);
            ResetLineNumbers();
        }

        SelectedLine = Lines.Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanMoveLineUp))]
    private void MoveLineUp(SalesOrderLineModel? line)
    {
        if (line is null)
        {
            return;
        }

        var index = Lines.Items.IndexOf(line);
        if (index <= 0)
        {
            return;
        }

        using (Lines.SuppressListEvents())
        {
            if (!Lines.Move(line, index - 1))
            {
                return;
            }

            ResetLineNumbers();
        }

        SelectedLine = line;
    }

    [RelayCommand(CanExecute = nameof(CanMoveLineDown))]
    private void MoveLineDown(SalesOrderLineModel? line)
    {
        if (line is null)
        {
            return;
        }

        var index = Lines.Items.IndexOf(line);
        if (index < 0 || index >= Lines.Items.Count - 1)
        {
            return;
        }

        using (Lines.SuppressListEvents())
        {
            if (!Lines.Move(line, index + 1))
            {
                return;
            }

            ResetLineNumbers();
        }

        SelectedLine = line;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // 首次写入仍受只读门禁保护；已发出的原请求允许在编辑冻结期间查询回执。
        if (_pendingSave is null && IsReadOnly)
        {
            await HandleReadOnlyViolationAsync();
            return;
        }
        await RunBusyAsync(async () =>
        {
            if (_pendingSave is null)
            {
                var headerError = Header.ValidateModel();
                if (!string.IsNullOrWhiteSpace(headerError))
                {
                    ValidationSummary = headerError;
                    HasValidationError = true;
                    _messageService.ShowWarning("销售订单未保存", headerError);
                    return;
                }

                if (Lines.Items.Count == 0)
                {
                    ValidationSummary = "销售订单至少需要一条明细。";
                    HasValidationError = true;
                    _messageService.ShowWarning("销售订单未保存", ValidationSummary);
                    return;
                }

                var lineError = await Lines.ValidateAllAsync();
                if (!string.IsNullOrWhiteSpace(lineError))
                {
                    ValidationSummary = lineError;
                    HasValidationError = true;
                    _messageService.ShowWarning("销售订单未保存", lineError);
                    return;
                }

                var snapshot = Lines.CreateSaveRequest(Header);
                snapshot.CommandId = Guid.NewGuid();
                _pendingSave = JsonSerializer.Deserialize<SaveSalesOrderRequest>(JsonSerializer.Serialize(snapshot))!;
                NotifyPendingStateChanged();
            }

            var request = _pendingSave!;
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.SaveAsync(request),
                "保存销售订单失败");
            if (!result.Success || result.Data?.Document is null)
            {
                if (!MustKeepIntent(result.Error?.Code, ref _saveOutcomeUnknown))
                {
                    _pendingSave = null;
                    _saveOutcomeUnknown = false;
                    NotifyPendingStateChanged();
                }
                _messageService.ShowError("保存销售订单失败", result.Message);
                return;
            }

            // 回执中的详情可能已被后来写入覆盖，只用其身份读取当前事实。
            var uid = result.Data.Document.Header.Uid;
            _navigationService.UpdateDocumentKey(this, $"sales-order:{uid:N}");
            _pendingSave = null;
            _saveOutcomeUnknown = false;
            SetRefreshOnly(uid);
            await RefreshCurrentFactAsync("销售订单已保存");
        }, "正在保存销售订单...");
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (_pendingConfirmation is null && !await _dialogService.ConfirmAsync(
                "确认销售订单",
                "确认后将为每条订单行生成毛需求，但不会拆解 BOM。是否继续？",
                "确认"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            if (_pendingConfirmation is null) SetPendingConfirmation(CreateConfirmCommand());
            var request = _pendingConfirmation!;
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.ConfirmAsync(request.Uid, request),
                "确认销售订单失败");
            if (!result.Success || result.Data is null)
            {
                if (!MustKeepConfirmationIntent(result.Error?.Code))
                    SetPendingConfirmation(null);
                _messageService.ShowError("确认销售订单失败", result.Message);
                return;
            }
            // 已取得确认结果后仅重试查询，不再重发已成功的确认命令。
            SetPendingConfirmation(null);
            SetRefreshOnly(request.Uid);
            await RefreshCurrentFactAsync("销售订单已确认");
            _messageService.ShowSuccess("销售订单已确认", $"已生成 {result.Data.GrossDemandCount} 条毛需求。等待需求工作台拆解。");
        }, "正在确认销售订单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        if (_pendingCancellation is null)
        {
            var reason = await _cancellationDialogService.RequestReasonAsync(Header.OrderNo);
            if (reason is null) return;
            _pendingCancellation = new CancelSalesOrderConfirmationCommand
            {
                Uid = Header.Uid, Version = Header.Version, CommandId = Guid.NewGuid(), Reason = reason
            };
            NotifyPendingStateChanged();
        }

        await RunBusyAsync(async () =>
        {
            var request = _pendingCancellation!;
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.CancelConfirmationAsync(request.Uid, request),
                "取消销售订单确认失败");
            if (!result.Success || result.Data is null)
            {
                if (!MustKeepIntent(result.Error?.Code, ref _cancellationOutcomeUnknown))
                {
                    _pendingCancellation = null;
                    _cancellationOutcomeUnknown = false;
                    NotifyPendingStateChanged();
                }
                _messageService.ShowError("取消确认失败", result.Message);
                return;
            }

            _pendingCancellation = null;
            _cancellationOutcomeUnknown = false;
            SetRefreshOnly(request.Uid);
            await RefreshCurrentFactAsync("已取消确认");
            _messageService.ShowSuccess(
                "已取消确认",
                $"{DocumentTitle}：删除 {result.Data.HardDeletedGrossDemandCount} 条，作废保留 {result.Data.VoidedGrossDemandCount} 条毛需求。");
        }, "正在取消确认...");
    }

    [RelayCommand]
    private async Task CloseDocumentAsync()
    {
        var tab = _navigationService.OpenTabs.FirstOrDefault(item => ReferenceEquals(item.ContentViewModel, this));
        await _navigationService.CloseAsync(tab);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        _productLookupCancellation?.Cancel();
        _productLookupCancellation = null;
        ProductCandidates.Clear();
        SelectedProductCandidate = null;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            IsProductLookupBusy = false;
            return;
        }

        var requestCancellation = new CancellationTokenSource();
        _productLookupCancellation = requestCancellation;
        IsProductLookupBusy = true;
        var token = requestCancellation.Token;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.SearchProductsAsync(new SalesOrderProductSearchRequest
                {
                    Keyword = keyword.Trim(),
                    CustomerId = Header.CustomerId == Guid.Empty ? null : Header.CustomerId,
                    PageIndex = 1,
                    PageSize = 30,
                }, token),
                token,
                "查询销售产品失败");
            if (!token.IsCancellationRequested && result.Success && result.Data is not null)
            {
                foreach (var product in result.Data.Items)
                {
                    ProductCandidates.Add(product);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_productLookupCancellation, requestCancellation))
            {
                _productLookupCancellation = null;
                IsProductLookupBusy = false;
            }

            requestCancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CommitProduct(SalesOrderLineModel? line)
    {
        if (line is null || SelectedProductCandidate is null || line.IsNonStandard)
        {
            return;
        }

        line.ApplyProduct(SelectedProductCandidate);
        SelectedProductCandidate = null;
        ProductCandidates.Clear();
        UpdateChangeState();
    }

    [RelayCommand]
    private void ClearProduct(SalesOrderLineModel? line)
    {
        if (line is null || line.IsNonStandard)
        {
            return;
        }

        line.ProductId = null;
        line.CustomerProductId = null;
        line.Sku = string.Empty;
        line.LinkSku = string.Empty;
        line.DescriptionEn = string.Empty;
        line.ColorNameEn = string.Empty;
        line.FeaturesEn = string.Empty;
        line.MainImage = string.Empty;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchCustomersAsync(string? keyword)
    {
        _customerLookupCancellation?.Cancel();
        _customerLookupCancellation = null;
        CustomerCandidates.Clear();
        SelectedCustomerCandidate = null;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            IsCustomerLookupBusy = false;
            return;
        }

        var requestCancellation = new CancellationTokenSource();
        _customerLookupCancellation = requestCancellation;
        IsCustomerLookupBusy = true;
        var token = requestCancellation.Token;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.SearchCustomersAsync(new SalesOrderCustomerSearchRequest
                {
                    Keyword = keyword.Trim(),
                    PageIndex = 1,
                    PageSize = 30,
                }, token),
                token,
                "查询客户失败");
            if (!token.IsCancellationRequested && result.Success && result.Data is not null)
            {
                foreach (var customer in result.Data.Items)
                {
                    CustomerCandidates.Add(customer);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_customerLookupCancellation, requestCancellation))
            {
                _customerLookupCancellation = null;
                IsCustomerLookupBusy = false;
            }

            requestCancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CommitCustomer()
    {
        var customer = SelectedCustomerCandidate;
        if (customer is null)
        {
            return;
        }

        Header.CustomerId = customer.CustomerId;
        Header.CustomerCode = customer.CustomerCode ?? string.Empty;
        Header.Currency = string.IsNullOrWhiteSpace(customer.Currency) ? Header.Currency : customer.Currency;
        Header.PaymentMethod = customer.PaymentMethod ?? string.Empty;
        Header.ShippedBy = customer.ShippedBy ?? string.Empty;
        Header.Incoterm = customer.Incoterm ?? string.Empty;
        Header.IncotermSortRemark = customer.IncotermSortRemark ?? string.Empty;
        Header.IncotermLocation = customer.IncotermLocation ?? string.Empty;
        Header.DepositRate = customer.DepositRate;
        Header.LeadTimeDays = customer.LeadTimeDays;
        Header.ShipToName = customer.ShipToName ?? string.Empty;
        Header.ShipToAddress = customer.ShipToAddress ?? string.Empty;
        Header.ShipToPhone = customer.ShipToPhone ?? string.Empty;
        SelectedCustomerCandidate = null;
        CustomerCandidates.Clear();
    }

    private void InitializeNewDocument()
    {
        _loadingDocument = true;
        Header.Load(new SalesOrderEditHeaderDto
        {
            DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.待确认,
            Currency = "USD",
            ExchangeRate = 1m,
            OrderDate = DateTime.Today,
        });
        Lines.Clear();
        SelectedLine = null;
        _loadingDocument = false;
        ApplyReadOnlyState();
        IsDirty = false;
        ValidationSummary = null;
        HasValidationError = false;
        NotifyDocumentStateChanged();
    }

    private void LoadDocument(SalesOrderDetailDto document)
    {
        _loadingDocument = true;
        Header.Load(document.Header);
        Lines.LoadFromDtos(document.Lines);
        SelectedLine = Lines.Items.FirstOrDefault();
        _loadingDocument = false;
        ApplyReadOnlyState();
        IsDirty = false;
        ValidationSummary = null;
        HasValidationError = false;
        NotifyDocumentStateChanged();
    }

    /// <summary>为当前详情的一次确认生成非空幂等命令标识。</summary>
    private ConfirmSalesOrderCommand CreateConfirmCommand()
    {
        return new ConfirmSalesOrderCommand { Uid = Header.Uid, Version = Header.Version, CommandId = Guid.NewGuid() };
    }

    /// <summary>前置拒绝只说明本次重试未执行，不能证明先前结果未知的确认未提交。</summary>
    private bool MustKeepConfirmationIntent(string? code) =>
        (_confirmationOutcomeUnknown |= code is ServiceErrorCodes.OutcomeUnknown
            or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable) && code is
        ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.RateLimited
        or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden or ServiceErrorCodes.ServiceUnavailable;

    /// <summary>首次前置拒绝结束新意图；已有未知结果时继续保留原请求。</summary>
    private static bool MustKeepIntent(string? code, ref bool outcomeUnknown)
    {
        outcomeUnknown |= code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy
            or ServiceErrorCodes.ServiceUnavailable;
        return outcomeUnknown && code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy
            or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.RateLimited
            or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden;
    }

    /// <summary>成功回执只保存目标身份；详情查询失败时冻结旧表单并提供只读重试。</summary>
    private void SetRefreshOnly(Guid uid)
    {
        _refreshOnlyUid = uid;
        NotifyPendingStateChanged();
    }

    /// <summary>重试成功后的当前详情查询，不再次发送已经确认的写命令。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryCurrentFact))]
    private Task RetryCurrentFactAsync() => RunBusyAsync(
        () => RefreshCurrentFactAsync("销售订单当前详情已刷新"), "正在刷新销售订单...");

    private bool CanRetryCurrentFact() => IsNotBusy && _refreshOnlyUid.HasValue;

    /// <summary>只有当前详情读取成功才解除写后冻结，避免把历史回执当作最新版本。</summary>
    private async Task RefreshCurrentFactAsync(string success)
    {
        if (_refreshOnlyUid is not { } uid) return;
        var current = await _apiCallRunner.RunAsync(() => _detailApi.GetAsync(uid), "刷新销售订单失败");
        if (!current.Success || current.Data is null)
        {
            _messageService.ShowWarning("操作已完成，仅刷新详情失败", current.Message);
            return;
        }
        LoadDocument(current.Data);
        _refreshOnlyUid = null;
        NotifyPendingStateChanged();
        _messageService.ShowSuccess(success, DocumentTitle);
    }

    /// <summary>统一通知表单只读、重试入口、页签状态和所有写命令。</summary>
    private void NotifyPendingStateChanged()
    {
        ApplyReadOnlyState();
        OnPropertyChanged(nameof(HasPendingIntent));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RetryCurrentFactCommand.NotifyCanExecuteChanged();
    }

    /// <summary>切换未决身份时同步通知表单、表格、页签关闭及所有相关命令门禁。</summary>
    private void SetPendingConfirmation(ConfirmSalesOrderCommand? request)
    {
        _pendingConfirmation = request;
        if (request is null) _confirmationOutcomeUnknown = false;
        NotifyPendingStateChanged();
    }

    private void ApplyReadOnlyState()
    {
        IsReadOnly = !CanMaintain || HasPendingIntent
            || Header.DocumentStatus == SalesOrderDocumentStatusEnum.Closed
            || Header.ConfirmationStatus == ConfirmationStatusEnum.已确认;
        OnPropertyChanged(nameof(IsEditable));
        NotifyDocumentCommands();
    }

    private void OnHeaderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDocument)
        {
            return;
        }

        UpdateChangeState();
        if (e.PropertyName == nameof(SalesOrderHeaderEditModel.OrderNo))
        {
            OnPropertyChanged(nameof(DocumentTitle));
        }
    }

    private void OnLinesStateChanged(object? sender, ListStateChangedEventArgs<SalesOrderLineModel> e)
    {
        if (!_loadingDocument)
        {
            UpdateChangeState();
        }

        OnPropertyChanged(nameof(TotalQty));
        OnPropertyChanged(nameof(TotalCnyAmount));
        OnPropertyChanged(nameof(TotalUsdAmount));
        NotifyLineReorderCommands();
    }

    private void UpdateChangeState()
    {
        IsDirty = true;
        ValidationSummary = null;
        HasValidationError = false;
        NotifyDocumentStateChanged();
    }

    private void NotifyDocumentStateChanged()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsPersisted));
        NotifyDocumentCommands();
    }

    private void NotifyDocumentCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
        CancelConfirmationCommand.NotifyCanExecuteChanged();
        AddStandardLineCommand.NotifyCanExecuteChanged();
        AddNonStandardLineCommand.NotifyCanExecuteChanged();
        DeleteSelectedLineCommand.NotifyCanExecuteChanged();
        NotifyLineReorderCommands();
    }

    private void ResetLineNumbers()
    {
        for (var index = 0; index < Lines.Items.Count; index++)
        {
            Lines.Items[index].LineNumber = index + 1;
        }
    }

    private void NotifyLineReorderCommands()
    {
        MoveLineUpCommand.NotifyCanExecuteChanged();
        MoveLineDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanEdit() => CanMaintain && CanWrite && IsNotBusy && !IsReadOnly && !HasPendingIntent;
    private bool CanDeleteLine() => CanEdit() && SelectedLine is not null;
    private bool CanMoveLineUp(SalesOrderLineModel? line) =>
        CanEdit() && line is not null && Lines.Items.IndexOf(line) > 0;
    private bool CanMoveLineDown(SalesOrderLineModel? line)
    {
        if (!CanEdit() || line is null)
        {
            return false;
        }

        var index = Lines.Items.IndexOf(line);
        return index >= 0 && index < Lines.Items.Count - 1;
    }
    private bool CanSave() => CanMaintain && IsNotBusy && (_pendingSave is not null || CanEdit() && IsDirty);
    /// <summary>确认权限独立于草稿维护权限；只读查看者仍可确认无本地修改的已保存订单。</summary>
    private bool CanConfirm() => CanConfirmOrder && IsNotBusy && (_pendingConfirmation is not null ||
        IsPersisted
        && !IsDirty
        && Header.DocumentStatus == SalesOrderDocumentStatusEnum.Open
        && Header.ConfirmationStatus == ConfirmationStatusEnum.待确认 && !HasPendingIntent);
    private bool CanCancelConfirmation() => CanCancelOrderConfirmation && IsNotBusy
        && (_pendingCancellation is not null || !HasPendingIntent
        && IsPersisted
        && Header.DocumentStatus == SalesOrderDocumentStatusEnum.Open
        && Header.ConfirmationStatus == ConfirmationStatusEnum.已确认);
}

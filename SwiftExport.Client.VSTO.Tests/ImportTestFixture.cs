using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Client.VSTO.Features.Sales.Orders;
using SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels;
using SwiftExport.Client.VSTO.Excel.Reading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Testing
{
    /// <summary>纯内存销售导入夹具；可被 STA 测试复用，不加载 Excel 或访问真实 API。</summary>
    internal sealed class ImportTestFixture : IDisposable, ISalesOrderImportPrompt, IUiDispatcher, ITokenStore, IExcelRangeReader
    {
        internal readonly ImportCache Cache = new ImportCache();
        internal readonly ImportLegacyMappingApi MappingApi = new ImportLegacyMappingApi();
        internal readonly ImportApi Api = new ImportApi();
        internal readonly ImportCheckApi CheckApi = new ImportCheckApi();
        internal readonly AddressProfileApi ProfileApi = new AddressProfileApi();
        internal readonly AddressReaderStub AddressReader = new AddressReaderStub();
        internal readonly SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services.ExcelReadProfileService ProfileService;
        internal readonly SessionService Session;
        internal readonly SalesOrderImportViewModel Model;
        internal bool Confirm = true;
        internal int Prompts;
        internal Action OnConfirm = null;
        internal int Selections;
        internal Func<bool, Task<ExcelRangeSnapshot>> Select = _ => Task.FromResult<ExcelRangeSnapshot>(null);
        internal Func<Action, Task> Dispatch = action => { action(); return Task.CompletedTask; };
        internal ImportTestFixture()
        {
            Session = new SessionService(this); Login("first");
            Cache.CustomerItems.Add(new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C1", CustomerName = "甲", Currency = "USD", Address = "地址", PhoneNumber = "123", PaymentMethod = "TT" });
            Cache.CurrencyItems.Add(new SysDicCurrencyDto { CurrencyCode = "USD", IsEnabled = true });
            Cache.DictionaryItems.Add(new SysDictDataDto { DictCode = "PaymentMethod", DataNameEn = "TT", IsEnabled = true });
            Cache.DictionaryItems.Add(new SysDictDataDto { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = true });
            ProfileService = new SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services.ExcelReadProfileService(ProfileApi);
            Model = new SalesOrderImportViewModel(Cache, Session, this, Api, new ApiCallRunner(new AddInLog()), this, this,
                new SalesOrderExcelParser(ExcelImportRegistry.CreateDefault()), MappingApi,
                ProfileService, AddressReader, null, CheckApi);
        }
        internal void Login(string id) => Session.TryCommit(Session.Capture().Version, "test-token", new MeDto { UserId = id, UserName = id });
        internal async Task Start()
        {
            await Model.InitializeAsync(); Model.SelectedCustomer = Cache.Customers[0]; Model.CustomerSelectedCommand.Execute(null);
        }
        /// <summary>测试线程直接接纳，STA 场景在同一 Dispatcher 消息循环运行。</summary>
        public Task InvokeAsync(Action action) => Dispatch(action);
        /// <summary>模拟区域选择的取消、延迟和快照，不加载 Excel。</summary>
        public Task<ExcelRangeSnapshot> SelectAsync(bool detail) { Selections++; return Select(detail); }
        /// <summary>返回测试设定的确认结果并计数。</summary>
        public bool ConfirmDiscard(string message) { Prompts++; OnConfirm?.Invoke(); return Confirm; }
        public string Read() => null;
        public void Write(string token) { }
        public void Clear() { }
        /// <summary>终止页面并释放会话。</summary>
        public void Dispose() { Model.Dispose(); Session.Dispose(); }
    }

    /// <summary>只读检查替身，测试显式提供每次响应和取消行为。</summary>
    internal sealed class ImportCheckApi : SwiftExport.Contracts.Apis.ForExcel.IForExcelSalesOrderApi
    {
        internal Func<SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportRequest, CancellationToken,
            Task<ServiceResult<SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportResponse>>> Check = (r, t) => throw new NotSupportedException();
        public Task<ServiceResult<SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportResponse>> CheckAsync(
            SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportRequest request, CancellationToken cancellationToken = default(CancellationToken)) => Check(request, cancellationToken);
    }

    /// <summary>返回可控基础资料快照，用于验证加载、失败及刷新绑定。</summary>
    internal sealed class ImportCache : IReferenceDataCacheService
    {
        internal readonly ObservableCollection<CustomerDto> CustomerItems = new ObservableCollection<CustomerDto>();
        internal readonly ObservableCollection<SysDicCurrencyDto> CurrencyItems = new ObservableCollection<SysDicCurrencyDto>();
        internal readonly ObservableCollection<SysDictDataDto> DictionaryItems = new ObservableCollection<SysDictDataDto>();
        internal int Loads;
        internal Func<Task<CacheRefreshResult>> Load = () => Task.FromResult(CacheRefreshResult.Ok());
        internal ImportCache()
        {
            Customers = new ReadOnlyObservableCollection<CustomerDto>(CustomerItems);
            Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>(CurrencyItems);
            DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(DictionaryItems);
        }
        public ReadOnlyObservableCollection<CustomerDto> Customers { get; }
        public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }
        public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }
        public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; } = Empty<ProductColorDto>();
        public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; } = Empty<SupplierDto>();
        public ReadOnlyObservableCollection<WarehouseSitePickerDto> WarehouseSites { get; } = Empty<WarehouseSitePickerDto>();
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; } = Empty<ProductCategoryCacheNode>();
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; } = Empty<ProductCategoryCacheNode>();
        public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; } = Empty<SysDictTypeDto>();
        private static ReadOnlyObservableCollection<T> Empty<T>() => new ReadOnlyObservableCollection<T>(new ObservableCollection<T>());
        public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string code, bool enabledOnly = true) => DictionaryItems.Where(d => d.DictCode == code && (!enabledOnly || d.IsEnabled)).ToArray();
        public Task<CacheRefreshResult> EnsureLoadedAsync() { Loads++; return Load(); }
        public Task<CacheRefreshResult> RefreshAllAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshProductColorsAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshCustomersAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshSuppliersAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshWarehouseSitesAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshCurrenciesAsync() => EnsureLoadedAsync();
        public Task<CacheRefreshResult> RefreshDictionariesAsync() => EnsureLoadedAsync();
        public void Clear() { CustomerItems.Clear(); CurrencyItems.Clear(); DictionaryItems.Clear(); }
    }

    /// <summary>旧链路的直接 API 测试替身，不向基础资料缓存添加映射能力。</summary>
    internal sealed class ImportLegacyMappingApi : SwiftExport.Contracts.Apis.SystemApis.IExcelFieldMappingApi
    {
        internal readonly List<ExcelFieldMappingDto> Mappings = new List<ExcelFieldMappingDto>();
        internal Func<Task<CacheRefreshResult>> Load = () => Task.FromResult(CacheRefreshResult.Ok());
        /// <summary>模拟每次直接读取旧配置，用于旧解析兼容性回归。</summary>
        public async Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, bool includeDisabled = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            var loaded = await Load();
            return loaded.Success ? ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Ok(Mappings.ToArray())
                : ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Fail(loaded.Message);
        }
        public Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync(CreateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(string groupCode, SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
    }

    /// <summary>提供显式设定的产品搜索与草稿保存响应；其他生命周期 API 仍禁止。</summary>
    internal sealed class ImportApi : ISalesOrderDetailApi
    {
        internal Func<SalesOrderProductSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>>> Search = (r, t) => throw new NotSupportedException("测试必须显式提供查询响应。");
        public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken)) => Search(request, cancellationToken);
        public Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        internal Func<SaveSalesOrderRequest, Task<ServiceResult<SaveSalesOrderResponse>>> Save = _ => throw new NotSupportedException("测试必须显式提供保存响应。");
        public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request) => Save(request);
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, ConfirmSalesOrderCommand request) => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, CancelSalesOrderConfirmationCommand request) => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request) => throw new NotSupportedException();
    }
}

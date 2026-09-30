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
    internal sealed class ImportTestFixture : IDisposable, ISalesOrderImportPrompt, IUiDispatcher, ITokenStore
    {
        internal readonly ImportCache Cache = new ImportCache();
        internal readonly ImportApi Api = new ImportApi();
        internal readonly SessionService Session;
        internal readonly SalesOrderImportViewModel Model;
        internal bool Confirm = true;
        internal int Prompts;
        internal ImportTestFixture()
        {
            Session = new SessionService(this); Login("first");
            Cache.CustomerItems.Add(new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C1", CustomerName = "甲", Currency = "USD", Address = "地址", PhoneNumber = "123", PaymentMethod = "TT" });
            Cache.CurrencyItems.Add(new SysDicCurrencyDto { CurrencyCode = "USD", IsEnabled = true });
            Cache.DictionaryItems.Add(new SysDictDataDto { DictCode = "PaymentMethod", DataNameEn = "TT", IsEnabled = true });
            Cache.DictionaryItems.Add(new SysDictDataDto { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = true });
            Model = new SalesOrderImportViewModel(Cache, Session, this, Api, new ApiCallRunner(new AddInLog()), this);
        }
        internal void Login(string id) => Session.TryCommit(Session.Capture().Version, "test-token", new MeDto { UserId = id, UserName = id });
        internal async Task Start()
        {
            await Model.InitializeAsync(); Model.SelectedCustomer = Cache.Customers[0]; Model.CustomerSelectedCommand.Execute(null);
        }
        /// <summary>测试线程直接接纳，STA 场景在同一 Dispatcher 消息循环运行。</summary>
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        /// <summary>返回测试设定的确认结果并计数。</summary>
        public bool ConfirmDiscard(string message) { Prompts++; return Confirm; }
        public string Read() => null;
        public void Write(string token) { }
        public void Clear() { }
        /// <summary>终止页面并释放会话。</summary>
        public void Dispose() { Model.Dispose(); Session.Dispose(); }
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
        /// <summary>导入测试不依赖字段映射，返回空快照。</summary>
        public IReadOnlyList<SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings.ExcelFieldMappingDto> GetExcelFieldMappings(string groupCode) => new SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings.ExcelFieldMappingDto[0];
        /// <summary>模拟已完成的分组配置加载。</summary>
        public Task<CacheRefreshResult> EnsureExcelFieldMappingsLoadedAsync(string groupCode) => Task.FromResult(CacheRefreshResult.Ok());
        /// <summary>模拟已完成的分组配置刷新。</summary>
        public Task<CacheRefreshResult> RefreshExcelFieldMappingsAsync(string groupCode) => Task.FromResult(CacheRefreshResult.Ok());
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

    /// <summary>只允许产品搜索，任何误接入保存或生命周期 API 都立即使测试失败。</summary>
    internal sealed class ImportApi : ISalesOrderDetailApi
    {
        internal Func<SalesOrderProductSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>>> Search = (r, t) => throw new NotSupportedException("测试必须显式提供查询响应。");
        public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken)) => Search(request, cancellationToken);
        public Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, ConfirmSalesOrderCommand request) => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, CancelSalesOrderConfirmationCommand request) => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request) => throw new NotSupportedException();
    }
}

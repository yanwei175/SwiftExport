using System.Collections.ObjectModel;
using System.Reflection;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>提供离线会话缓存和可控刷新结果，避免客户端测试依赖真实 API。</summary>
internal sealed class ReferenceDataCacheServiceStub : IReferenceDataCacheService
{
    private readonly ObservableCollection<SysDictDataDto> _dictionaryValues;
    private readonly ObservableCollection<CustomerDto> _customers;
    private readonly ObservableCollection<SupplierDto> _suppliers;
    private readonly ObservableCollection<WarehouseSitePickerDto> _warehouseSites;
    private readonly CacheRefreshResult _ensureResult;
    private readonly CacheRefreshResult _supplierRefreshResult;
    private readonly CacheRefreshResult _warehouseRefreshResult;

    /// <summary>用指定基础资料快照和刷新结果初始化离线缓存。</summary>
    public ReferenceDataCacheServiceStub(
        IEnumerable<SysDictDataDto>? dictionaryValues = null,
        IEnumerable<SupplierDto>? suppliers = null,
        IEnumerable<WarehouseSitePickerDto>? warehouseSites = null,
        bool ensureSuccess = true,
        string ensureMessage = "测试缓存已加载。",
        bool supplierRefreshSuccess = true,
        string supplierRefreshMessage = "测试供应商缓存已加载。",
        bool warehouseRefreshSuccess = true,
        string warehouseRefreshMessage = "测试仓库缓存已加载。",
        IEnumerable<CustomerDto>? customers = null)
    {
        _dictionaryValues = new ObservableCollection<SysDictDataDto>(dictionaryValues ?? []);
        _customers = new ObservableCollection<CustomerDto>(customers ?? []);
        _suppliers = new ObservableCollection<SupplierDto>(suppliers ?? []);
        _warehouseSites = new ObservableCollection<WarehouseSitePickerDto>(warehouseSites ?? []);
        _ensureResult = CreateRefreshResult(ensureSuccess, ensureMessage);
        _supplierRefreshResult = CreateRefreshResult(supplierRefreshSuccess, supplierRefreshMessage);
        _warehouseRefreshResult = CreateRefreshResult(warehouseRefreshSuccess, warehouseRefreshMessage);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>([]);
        Customers = new ReadOnlyObservableCollection<CustomerDto>(_customers);
        Suppliers = new ReadOnlyObservableCollection<SupplierDto>(_suppliers);
        WarehouseSites = new ReadOnlyObservableCollection<WarehouseSitePickerDto>(_warehouseSites);
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>([]);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
        DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>([]);
        DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
    }

    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }
    public ReadOnlyObservableCollection<CustomerDto> Customers { get; }
    public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }
    public ReadOnlyObservableCollection<WarehouseSitePickerDto> WarehouseSites { get; }
    public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }
    public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }
    public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }
    public int EnsureLoadedCallCount { get; private set; }
    public int RefreshSuppliersCallCount { get; private set; }
    public int EnsureWarehouseSitesLoadedCallCount { get; private set; }
    public int RefreshWarehouseSitesCallCount { get; private set; }
    public int RefreshDictionariesCallCount { get; private set; }

    public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true) =>
        DictionaryValues
            .Where(item => string.Equals(item.DictCode, dictCode, StringComparison.OrdinalIgnoreCase)
                && (!enabledOnly || item.IsEnabled))
            .ToArray();

    public void AddDictionaryValue(SysDictDataDto item) => _dictionaryValues.Add(item);

    public Task<CacheRefreshResult> EnsureLoadedAsync()
    {
        EnsureLoadedCallCount++;
        return Task.FromResult(_ensureResult);
    }
    public Task<CacheRefreshResult> RefreshAllAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshProductColorsAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshCustomersAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshSuppliersAsync()
    {
        RefreshSuppliersCallCount++;
        return Task.FromResult(_supplierRefreshResult);
    }
    public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync()
    {
        EnsureWarehouseSitesLoadedCallCount++;
        return Task.FromResult(_warehouseRefreshResult);
    }
    public Task<CacheRefreshResult> RefreshWarehouseSitesAsync()
    {
        RefreshWarehouseSitesCallCount++;
        return Task.FromResult(_warehouseRefreshResult);
    }
    public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshCurrenciesAsync() => throw new NotSupportedException();
    /// <summary>字典刷新沿用离线成功或失败结果，统计调用；不向实际服务发送请求。</summary>
    public Task<CacheRefreshResult> RefreshDictionariesAsync()
    {
        RefreshDictionariesCallCount++;
        return Task.FromResult(_ensureResult);
    }

    /// <summary>清除测试会话的可变基础资料集合，模拟登录会话结束。</summary>
    public void Clear()
    {
        _dictionaryValues.Clear();
        _customers.Clear();
        _suppliers.Clear();
        _warehouseSites.Clear();
    }

    /// <summary>测试程序集通过私有构造函数创建缓存结果，避免为测试扩大生产 API。</summary>
    private static CacheRefreshResult CreateRefreshResult(bool success, string message) =>
        (CacheRefreshResult)(Activator.CreateInstance(
            typeof(CacheRefreshResult),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [success, message],
            culture: null) ?? throw new InvalidOperationException("无法创建缓存测试结果。"));
}

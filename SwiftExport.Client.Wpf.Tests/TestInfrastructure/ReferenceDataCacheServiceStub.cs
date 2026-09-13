using System.Collections.ObjectModel;
using System.Reflection;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

internal sealed class ReferenceDataCacheServiceStub : IReferenceDataCacheService
{
    private readonly ObservableCollection<SysDictDataDto> _dictionaryValues;
    private readonly ObservableCollection<SupplierDto> _suppliers;
    private readonly CacheRefreshResult _ensureResult;

    public ReferenceDataCacheServiceStub(
        IEnumerable<SysDictDataDto>? dictionaryValues = null,
        IEnumerable<SupplierDto>? suppliers = null,
        bool ensureSuccess = true,
        string ensureMessage = "测试缓存已加载。")
    {
        _dictionaryValues = new ObservableCollection<SysDictDataDto>(dictionaryValues ?? []);
        _suppliers = new ObservableCollection<SupplierDto>(suppliers ?? []);
        _ensureResult = CreateRefreshResult(ensureSuccess, ensureMessage);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>([]);
        Customers = new ReadOnlyObservableCollection<CustomerDto>([]);
        Suppliers = new ReadOnlyObservableCollection<SupplierDto>(_suppliers);
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>([]);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
        DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>([]);
        DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
    }

    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }
    public ReadOnlyObservableCollection<CustomerDto> Customers { get; }
    public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }
    public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }
    public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }
    public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }
    public int EnsureLoadedCallCount { get; private set; }

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
    public Task<CacheRefreshResult> RefreshSuppliersAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshCurrenciesAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshDictionariesAsync() => throw new NotSupportedException();

    public void Clear()
    {
        _dictionaryValues.Clear();
        _suppliers.Clear();
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

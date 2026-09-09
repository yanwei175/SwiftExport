using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

internal sealed class ReferenceDataCacheServiceStub : IReferenceDataCacheService
{
    private readonly ObservableCollection<SysDictDataDto> _dictionaryValues;

    public ReferenceDataCacheServiceStub(IEnumerable<SysDictDataDto>? dictionaryValues = null)
    {
        _dictionaryValues = new ObservableCollection<SysDictDataDto>(dictionaryValues ?? []);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>([]);
        Customers = new ReadOnlyObservableCollection<CustomerDto>([]);
        Suppliers = new ReadOnlyObservableCollection<SupplierDto>([]);
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

    public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true) =>
        DictionaryValues
            .Where(item => string.Equals(item.DictCode, dictCode, StringComparison.OrdinalIgnoreCase)
                && (!enabledOnly || item.IsEnabled))
            .ToArray();

    public void AddDictionaryValue(SysDictDataDto item) => _dictionaryValues.Add(item);

    public Task<CacheRefreshResult> EnsureLoadedAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshAllAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshProductColorsAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshCustomersAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshSuppliersAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshCurrenciesAsync() => throw new NotSupportedException();
    public Task<CacheRefreshResult> RefreshDictionariesAsync() => throw new NotSupportedException();

    public void Clear() => _dictionaryValues.Clear();
}

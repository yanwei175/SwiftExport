using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;
using System.Windows;

namespace SwiftExportClient.Services.Caching;

/// <summary>
/// 基础资料内存缓存实现。服务以 Singleton 注册，生命周期限定为当前登录会话。
/// </summary>
public sealed class ReferenceDataCacheService : IReferenceDataCacheService, IDisposable
{
    private const int MasterDataPageSize = 200;
    private readonly ICustomerApi _customerApi;
    private readonly ISupplierApi _supplierApi;
    private readonly IWarehouseSiteApi _warehouseSiteApi;
    private readonly IProductColorApi _productColorApi;
    private readonly IProductCategoryApi _productCategoryApi;
    private readonly IProductAttributeApi _productAttributeApi;
    private readonly ISysDicCurrencyApi _currencyApi;
    private readonly ISysDictTypeApi _dictTypeApi;
    private readonly ISysDictDataApi _dictDataApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly ISessionService _sessionService;
    private readonly CacheObservableCollection<ProductColorDto> _productColors = [];
    private readonly CacheObservableCollection<CustomerDto> _customers = [];
    private readonly CacheObservableCollection<SupplierDto> _suppliers = [];
    private readonly CacheObservableCollection<WarehouseSitePickerDto> _warehouseSites = [];
    private readonly CacheObservableCollection<SysDicCurrencyDto> _currencies = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategoryTree = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategories = [];
    private readonly CacheObservableCollection<ProductAttributeGroupDto> _productAttributeGroups = [];
    private readonly CacheObservableCollection<ProductAttributeProfileGroupDto> _productAttributeProfileRelations = [];
    private IReadOnlyList<SysDictDataDto> _profileSnapshot = [];
    private ProductAttributeDefinitionsDto _attributeSnapshot = new();
    private readonly CacheObservableCollection<SysDictTypeDto> _dictionaryTypes = [];
    private readonly CacheObservableCollection<SysDictDataDto> _dictionaryValues = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _ensureSync = new();
    private readonly object _warehouseEnsureSync = new();
    private readonly object _attributeEnsureSync = new();
    private Task<CacheRefreshResult>? _ensureTask;
    private Task<CacheRefreshResult>? _warehouseEnsureTask;
    private Task<CacheRefreshResult>? _attributeEnsureTask;
    // 当前共享刷新门的唯一等待 owner，覆盖标签及其他基础资料；会话结束时不等待底层 API 晚到。
    private CancellationTokenSource? _refreshCancellation;
    private volatile bool _attributesLoaded;
    private volatile bool _colorsLoaded;
    private volatile bool _customersLoaded;
    private volatile bool _suppliersLoaded;
    private volatile bool _warehouseSitesLoaded;
    private volatile bool _currenciesLoaded;
    private volatile bool _categoriesLoaded;
    private volatile bool _dictionariesLoaded;
    private long _sessionGeneration;
    private bool _disposed;

    /// <summary>注入基础资料 API 和会话事件，维护一次登录内的只读快照；不缓存产品选择草稿。</summary>
    public ReferenceDataCacheService(
        ICustomerApi customerApi,
        ISupplierApi supplierApi,
        IWarehouseSiteApi warehouseSiteApi,
        IProductColorApi productColorApi,
        IProductCategoryApi productCategoryApi,
        IProductAttributeApi productAttributeApi,
        ISysDicCurrencyApi currencyApi,
        ISysDictTypeApi dictTypeApi,
        ISysDictDataApi dictDataApi,
        IApiCallRunner apiCallRunner,
        ISessionService sessionService)
    {
        _customerApi = customerApi;
        _supplierApi = supplierApi;
        _warehouseSiteApi = warehouseSiteApi;
        _productColorApi = productColorApi;
        _productCategoryApi = productCategoryApi;
        _productAttributeApi = productAttributeApi;
        _currencyApi = currencyApi;
        _dictTypeApi = dictTypeApi;
        _dictDataApi = dictDataApi;
        _apiCallRunner = apiCallRunner;
        _sessionService = sessionService;
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>(_productColors);
        Customers = new ReadOnlyObservableCollection<CustomerDto>(_customers);
        Suppliers = new ReadOnlyObservableCollection<SupplierDto>(_suppliers);
        WarehouseSites = new ReadOnlyObservableCollection<WarehouseSitePickerDto>(_warehouseSites);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>(_currencies);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategoryTree);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategories);
        ProductAttributeGroups = new ReadOnlyObservableCollection<ProductAttributeGroupDto>(_productAttributeGroups);
        ProductAttributeProfileRelations = new ReadOnlyObservableCollection<ProductAttributeProfileGroupDto>(_productAttributeProfileRelations);
        DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>(_dictionaryTypes);
        DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
        _sessionService.SessionEnded += OnSessionEnded;
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<CustomerDto> Customers { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<WarehouseSitePickerDto> WarehouseSites { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductAttributeGroupDto> ProductAttributeGroups { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductAttributeProfileGroupDto> ProductAttributeProfileRelations { get; }

    /// <inheritdoc />
    public bool AreProductAttributesLoaded => _attributesLoaded && _dictionariesLoaded;

    /// <inheritdoc />
    public event EventHandler? ProductAttributesChanged;

    /// <inheritdoc />
    public ProductAttributeResolution ResolveProductAttributes(Guid profileId)
    {
        if (!AreProductAttributesLoaded)
            return new(false, "属性模板字典或标签定义尚未完整加载，请刷新标签后重试。", [], []);
        return ProductAttributeProfileResolver.Resolve(profileId, _profileSnapshot, _attributeSnapshot.Groups, _attributeSnapshot.ProfileRelations);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProductAttributeGroupDto> GetProductAttributeGroups(Guid profileId)
    {
        var result = ResolveProductAttributes(profileId);
        return result.Success ? result.Groups : throw new InvalidOperationException(result.Message);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProductAttributeOptionDto> GetProductAttributeOptions(Guid groupId) =>
        _attributeSnapshot.Groups.FirstOrDefault(group => group.Uid == groupId)?.Options.ToArray() ?? [];

    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureProductAttributesLoadedAsync()
    {
        lock (_attributeEnsureSync)
        {
            if (_disposed) return Task.FromResult(CacheRefreshResult.Fail("缓存服务已经释放。"));
            if (AreProductAttributesLoaded)
                return Task.FromResult(CacheRefreshResult.Ok("产品属性缓存已经加载。"));

            if (_attributeEnsureTask is { IsCompleted: false })
                return _attributeEnsureTask;

            return _attributeEnsureTask = RefreshProductAttributesAsync();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshProductAttributesAsync()
    {
        // 请求在排队前记录会话身份，避免旧会话的排队刷新取得新会话身份后写入。
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            lock (_attributeEnsureSync)
            {
                if (_disposed || generation != Volatile.Read(ref _sessionGeneration))
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次产品属性缓存结果。");

                cancellation = new CancellationTokenSource();
                _refreshCancellation = cancellation;
            }

            var token = cancellation.Token;
            var definitionsTask = _apiCallRunner.RunAsync(
                () => _productAttributeApi.GetDefinitionsAsync(token), token, "加载产品属性缓存失败");
            var typesTask = LoadDictionaryTypesAsync();
            var valuesTask = LoadDictionaryValuesAsync();
            await Task.WhenAll(definitionsTask, typesTask, valuesTask).WaitAsync(token);
            var definitions = await definitionsTask;
            var types = await typesTask;
            var values = await valuesTask;
            if (!definitions.Success || !types.Success || !values.Success)
                return CacheRefreshResult.Fail(JoinFailureMessages(definitions.Success ? null : definitions.Message,
                    types.Success ? null : types.Message, values.Success ? null : values.Message));
            if (!TryPrepareProductAttributes(definitions.Data, types.Data, values.Data, out var prepared, out var error))
                return CacheRefreshResult.Fail(error);
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                    return;

                // 字典、组、值与模板关联全部准备完毕后发布，订阅者看到一致的适用范围。
                PublishProductAttributes(prepared!);
                applied = true;
                ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
            });
            return applied
                ? CacheRefreshResult.Ok("产品属性缓存已更新。")
                : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次产品属性缓存结果。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次产品属性缓存结果。");
        }
        finally
        {
            lock (_attributeEnsureSync)
            {
                if (ReferenceEquals(_refreshCancellation, cancellation))
                    _refreshCancellation = null;
            }
            cancellation?.Dispose();
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }

    /// <inheritdoc />
    public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true)
    {
        if (string.IsNullOrWhiteSpace(dictCode))
        {
            return [];
        }

        return DictionaryValues
            .Where(item => string.Equals(item.DictCode, dictCode.Trim(), StringComparison.OrdinalIgnoreCase)
                && (!enabledOnly || item.IsEnabled))
            .ToArray();
    }

    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureLoadedAsync()
    {
        lock (_ensureSync)
        {
            if (_disposed) return Task.FromResult(CacheRefreshResult.Fail("缓存服务已经释放。"));
            if (_colorsLoaded
                && _customersLoaded
                && _suppliersLoaded
                && _categoriesLoaded
                && _currenciesLoaded
                && _dictionariesLoaded
                && _attributesLoaded)
            {
                return Task.FromResult(CacheRefreshResult.Ok("缓存已经加载。"));
            }

            if (_ensureTask is { IsCompleted: false })
                return _ensureTask;

            return _ensureTask = EnsureLoadedCoreAsync();
        }
    }

    /// <inheritdoc />
    public Task<CacheRefreshResult> RefreshAllAsync() => RefreshWithAttributesAsync(forceRefresh: true);

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshProductColorsAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次颜色缓存结果。");
            var token = cancellation.Token;
            var result = await LoadColorsAsync().WaitAsync(token);
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _productColors.ReplaceAll(result.Data ?? []);
                _colorsLoaded = true;
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次颜色缓存结果。");
            }

            return CacheRefreshResult.Ok("产品颜色缓存已更新。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次颜色缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshCustomersAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次客户缓存结果。");
            var token = cancellation.Token;
            var result = await LoadCustomersAsync(token).WaitAsync(token);
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var customers = OrderCustomers(result.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _customers.ReplaceAll(customers);
                _customersLoaded = true;
                applied = true;
            });
            return applied
                ? CacheRefreshResult.Ok("客户缓存已更新。")
                : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次客户缓存结果。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次客户缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshSuppliersAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次供应商缓存结果。");
            var token = cancellation.Token;
            var result = await LoadSuppliersAsync(token).WaitAsync(token);
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var suppliers = OrderSuppliers(result.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _suppliers.ReplaceAll(suppliers);
                _suppliersLoaded = true;
                applied = true;
            });
            return applied
                ? CacheRefreshResult.Ok("供应商缓存已更新。")
                : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次供应商缓存结果。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次供应商缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync()
    {
        lock (_warehouseEnsureSync)
        {
            if (_warehouseSitesLoaded)
                return Task.FromResult(CacheRefreshResult.Ok("仓库缓存已经加载。"));

            if (_warehouseEnsureTask is { IsCompleted: false })
                return _warehouseEnsureTask;

            return _warehouseEnsureTask = RefreshWarehouseSitesAsync();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshWarehouseSitesAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次仓库缓存结果。");
            var token = cancellation.Token;
            var result = await LoadWarehouseSitesAsync(token).WaitAsync(token);
            if (!result.Success)
                return CacheRefreshResult.Fail(result.Message);

            var warehouses = OrderWarehouseSites(result.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                    return;

                _warehouseSites.ReplaceAll(warehouses);
                _warehouseSitesLoaded = true;
                applied = true;
            });
            return applied
                ? CacheRefreshResult.Ok("活动仓库缓存已更新。")
                : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次仓库缓存结果。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次仓库缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshProductCategoriesAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            lock (_attributeEnsureSync)
            {
                if (_disposed || generation != Volatile.Read(ref _sessionGeneration))
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次分类缓存结果。");
                cancellation = new CancellationTokenSource();
                _refreshCancellation = cancellation;
            }
            var token = cancellation.Token;
            var result = await LoadCategoriesAsync().WaitAsync(token);
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            if (!TryPrepareCategoryRows(result.Data, out var categories, out var error))
                return CacheRefreshResult.Fail(error);
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                PublishCategories(categories);
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次分类缓存结果。");
            }

            return CacheRefreshResult.Ok("产品分类缓存已更新。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次分类缓存结果。"); }
        finally
        {
            lock (_attributeEnsureSync)
                if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
            cancellation?.Dispose();
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshCurrenciesAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次币种缓存结果。");
            var token = cancellation.Token;
            var result = await LoadCurrenciesAsync().WaitAsync(token);
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var currencies = OrderCurrencies(result.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _currencies.ReplaceAll(currencies);
                _currenciesLoaded = true;
                applied = true;
            });
            return applied
                ? CacheRefreshResult.Ok("币种缓存已更新。")
                : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次币种缓存结果。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次币种缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshDictionariesAsync()
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            cancellation = BeginRefreshCancellation(generation);
            if (cancellation is null)
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次字典缓存结果。");
            var token = cancellation.Token;
            var typesTask = LoadDictionaryTypesAsync();
            var valuesTask = LoadDictionaryValuesAsync();
            await Task.WhenAll(typesTask, valuesTask).WaitAsync(token);

            var typesResult = await typesTask;
            var valuesResult = await valuesTask;
            if (!typesResult.Success || !valuesResult.Success)
            {
                return CacheRefreshResult.Fail(JoinFailureMessages(
                    typesResult.Success ? null : typesResult.Message,
                    valuesResult.Success ? null : valuesResult.Message));
            }

            if (!TryPrepareDictionaryRows(typesResult.Data, valuesResult.Data, out var types, out var values))
                return CacheRefreshResult.Fail("字典接口没有返回完整快照，旧数据已保留。");
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _dictionaryTypes.ReplaceAll(types);
                _dictionaryValues.ReplaceAll(values);
                _profileSnapshot = values.Where(IsProductAttributeProfile).Select(row => row.Adapt<SysDictDataDto>()).ToArray();
                _dictionariesLoaded = true;
                applied = true;
                ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次字典缓存结果。");
            }

            return CacheRefreshResult.Ok("字典类型和字典值缓存已更新。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次字典缓存结果。"); }
        finally { EndRefreshCancellation(cancellation); }
    }

    /// <inheritdoc />
    public void Clear()
    {
        Interlocked.Increment(ref _sessionGeneration);
        lock (_attributeEnsureSync)
        {
            _refreshCancellation?.Cancel();
            _attributesLoaded = false;
            _attributeEnsureTask = null;
        }
        RunOnUiThread(() =>
        {
            _profileSnapshot = [];
            _attributeSnapshot = new ProductAttributeDefinitionsDto();
            _productColors.ReplaceAll([]);
            _customers.ReplaceAll([]);
            _suppliers.ReplaceAll([]);
            _warehouseSites.ReplaceAll([]);
            _currencies.ReplaceAll([]);
            _productCategoryTree.ReplaceAll([]);
            _productCategories.ReplaceAll([]);
            _dictionaryTypes.ReplaceAll([]);
            _dictionaryValues.ReplaceAll([]);
            _productAttributeGroups.ReplaceAll([]);
            _productAttributeProfileRelations.ReplaceAll([]);
            ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
        });

        lock (_ensureSync)
        {
            _colorsLoaded = false;
            _customersLoaded = false;
            _suppliersLoaded = false;
            _currenciesLoaded = false;
            _categoriesLoaded = false;
            _dictionariesLoaded = false;
            _ensureTask = null;
        }

        lock (_warehouseEnsureSync)
        {
            _warehouseSitesLoaded = false;
            _warehouseEnsureTask = null;
        }
    }

    /// <summary>解除会话订阅并取消当前刷新等待；忽略取消的底层响应仍受代际与释放状态保护。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionService.SessionEnded -= OnSessionEnded;
        lock (_attributeEnsureSync)
            _refreshCancellation?.Cancel();
        // 排队或忽略取消的请求仍会在 finally 释放信号量；此托管对象交给 GC，避免释放期间 Dispose 竞争。
    }

    /// <summary>当前刷新已取得共享门后登记唯一等待 owner；旧会话排队请求不发出网络调用。</summary>
    private CancellationTokenSource? BeginRefreshCancellation(long generation)
    {
        lock (_attributeEnsureSync)
        {
            if (_disposed || generation != Volatile.Read(ref _sessionGeneration)) return null;
            var cancellation = new CancellationTokenSource();
            _refreshCancellation = cancellation;
            return cancellation;
        }
    }

    /// <summary>只清理当前请求拥有的取消源，并释放共享门；底层晚到响应不会再次进入发布流程。</summary>
    private void EndRefreshCancellation(CancellationTokenSource? cancellation)
    {
        lock (_attributeEnsureSync)
            if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
        cancellation?.Dispose();
        _refreshGate.Release();
    }

    /// <summary>首次加载复用同轮字典请求；其他基础资料已完整加载时只重试标签。</summary>
    private Task<CacheRefreshResult> EnsureLoadedCoreAsync() => RefreshWithAttributesAsync(forceRefresh: false);

    /// <summary>合并首次使用入口，并在标签失败时保持基础资料的独立成功状态。</summary>
    private async Task<CacheRefreshResult> RefreshWithAttributesAsync(bool forceRefresh)
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        if (forceRefresh || !_colorsLoaded || !_customersLoaded || !_suppliersLoaded
            || !_categoriesLoaded || !_currenciesLoaded || !_dictionariesLoaded)
            return await RefreshAllCoreAsync(forceRefresh);
        var attributes = await EnsureProductAttributesLoadedAsync();
        if (_disposed || generation != Volatile.Read(ref _sessionGeneration))
            return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
        return CacheRefreshResult.Ok(attributes.Success ? "基础资料和产品属性缓存已经加载。"
            : "基础资料缓存已经加载。产品属性缓存未更新，请在标签区域重试。");
    }

    /// <summary>同一刷新门内并行读取网络资料；字典模板与标签成对发布，其他基础资料保持独立失败边界。</summary>
    private async Task<CacheRefreshResult> RefreshAllCoreAsync(bool forceAttributes)
    {
        var generation = Volatile.Read(ref _sessionGeneration);
        await _refreshGate.WaitAsync();
        CancellationTokenSource? cancellation = null;
        try
        {
            lock (_attributeEnsureSync)
            {
                if (_disposed || generation != Volatile.Read(ref _sessionGeneration))
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
                cancellation = new CancellationTokenSource();
                _refreshCancellation = cancellation;
            }
            var token = cancellation.Token;
            var colorsTask = LoadColorsAsync();
            var customersTask = LoadCustomersAsync(token);
            var suppliersTask = LoadSuppliersAsync(token);
            var categoriesTask = LoadCategoriesAsync();
            var currenciesTask = LoadCurrenciesAsync();
            var dictionaryTypesTask = LoadDictionaryTypesAsync();
            var dictionaryValuesTask = LoadDictionaryValuesAsync();
            var definitionsTask = forceAttributes || !AreProductAttributesLoaded
                ? _apiCallRunner.RunAsync(() => _productAttributeApi.GetDefinitionsAsync(token), token, "加载产品属性缓存失败")
                : Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new ProductAttributeDefinitionsDto
                {
                    Groups = ProductAttributeGroups.ToList(),
                    ProfileRelations = ProductAttributeProfileRelations.ToList()
                }));
            await Task.WhenAll(
                colorsTask,
                customersTask,
                suppliersTask,
                categoriesTask,
                currenciesTask,
                dictionaryTypesTask,
                dictionaryValuesTask,
                definitionsTask).WaitAsync(token);

            var colorsResult = await colorsTask;
            var customersResult = await customersTask;
            var suppliersResult = await suppliersTask;
            var categoriesResult = await categoriesTask;
            var currenciesResult = await currenciesTask;
            var dictionaryTypesResult = await dictionaryTypesTask;
            var dictionaryValuesResult = await dictionaryValuesTask;
            var definitionsResult = await definitionsTask;
            var categoriesValid = categoriesResult.Success
                && TryPrepareCategoryRows(categoriesResult.Data, out _, out _);
            var dictionariesValid = dictionaryTypesResult.Success && dictionaryValuesResult.Success
                && TryPrepareDictionaryRows(dictionaryTypesResult.Data, dictionaryValuesResult.Data, out _, out _);
            PreparedProductAttributes? prepared = null;
            var attributesValid = dictionariesValid && definitionsResult.Success
                && TryPrepareProductAttributes(definitionsResult.Data, dictionaryTypesResult.Data, dictionaryValuesResult.Data, out prepared, out _);
            var baseValid = colorsResult.Success && customersResult.Success && suppliersResult.Success
                && categoriesValid && currenciesResult.Success && dictionariesValid;
            if (!baseValid && !attributesValid)
                return CacheRefreshResult.Fail(JoinFailureMessages(
                    colorsResult.Success ? null : colorsResult.Message,
                    customersResult.Success ? null : customersResult.Message,
                    suppliersResult.Success ? null : suppliersResult.Message,
                    categoriesValid ? null : categoriesResult.Message ?? "分类接口没有返回完整快照。",
                    currenciesResult.Success ? null : currenciesResult.Message,
                    dictionaryTypesResult.Success ? null : dictionaryTypesResult.Message,
                    dictionaryValuesResult.Success ? null : dictionaryValuesResult.Message,
                    definitionsResult.Success ? null : definitionsResult.Message));
            var customers = OrderCustomers(customersResult.Data ?? []).ToArray();
            var suppliers = OrderSuppliers(suppliersResult.Data ?? []).ToArray();
            var currencies = OrderCurrencies(currenciesResult.Data ?? []).ToArray();
            var dictionaryTypes = OrderDictionaryTypes(dictionaryTypesResult.Data ?? []).ToArray();
            var dictionaryValues = OrderDictionaryValues(dictionaryValuesResult.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                if (baseValid)
                {
                    _productColors.ReplaceAll(colorsResult.Data ?? []);
                    _customers.ReplaceAll(customers);
                    _suppliers.ReplaceAll(suppliers);
                    _currencies.ReplaceAll(currencies);
                    // 标签有效时由成对发布方法一次替换字典，避免集合通知提前暴露半套范围。
                    if (!attributesValid)
                    {
                        _dictionaryTypes.ReplaceAll(dictionaryTypes);
                        _dictionaryValues.ReplaceAll(dictionaryValues);
                    }
                    _colorsLoaded = true;
                    _customersLoaded = true;
                    _suppliersLoaded = true;
                    _currenciesLoaded = true;
                    _dictionariesLoaded = true;
                }
                if (baseValid)
                    PublishCategories(categoriesResult.Data!.Where(row => !row.IsDeleted).Select(row => row.Adapt<ProductCategoryDto>()).ToArray());
                if (attributesValid)
                    PublishProductAttributes(prepared!);
                applied = true;
                ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
            }

            if (!baseValid)
                return CacheRefreshResult.Fail(JoinFailureMessages(
                    colorsResult.Success ? null : colorsResult.Message,
                    customersResult.Success ? null : customersResult.Message,
                    suppliersResult.Success ? null : suppliersResult.Message,
                    currenciesResult.Success ? null : currenciesResult.Message,
                    dictionaryTypesResult.Success ? null : dictionaryTypesResult.Message,
                    dictionaryValuesResult.Success ? null : dictionaryValuesResult.Message));
            return CacheRefreshResult.Ok(attributesValid
                ? "产品颜色、客户、供应商、产品属性、币种和字典缓存已更新。"
                : "基础资料缓存已更新。产品属性缓存未更新，请在标签区域重试。");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        { return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。"); }
        finally
        {
            lock (_attributeEnsureSync)
                if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
            cancellation?.Dispose();
            _refreshGate.Release();
        }
    }

    /// <summary>字典和定义必须完整且身份关系一致，缺失集合或孤立关联不得发布为正常空数据。</summary>
    private static bool TryPrepareProductAttributes(ProductAttributeDefinitionsDto? definitions,
        IReadOnlyList<SysDictTypeDto>? types, IReadOnlyList<SysDictDataDto>? values,
        out PreparedProductAttributes? prepared, out string error)
    {
        prepared = null;
        error = "属性模板与标签接口没有返回完整定义，请重试。";
        if (!TryPrepareDictionaryRows(types, values, out var typeRows, out var valueRows)
            || definitions is null || definitions.ProfileId != Guid.Empty || definitions.Groups is null || definitions.ProfileRelations is null
            || definitions.Groups.Any(group => group is null || group.Uid == Guid.Empty || group.IsDeleted || group.Version <= 0 || group.Options is null)
            || definitions.ProfileRelations.Any(row => row is null || row.Uid == Guid.Empty || row.IsDeleted || row.Version <= 0)
            || definitions.Groups.Select(row => row.Uid).Distinct().Count() != definitions.Groups.Count) return false;
        var groupIds = definitions.Groups.Select(row => row.Uid).ToHashSet();
        var profileIds = valueRows.Where(IsProductAttributeProfile).Where(row => row.IsEnabled).Select(row => row.Uid).ToHashSet();
        var options = definitions.Groups.SelectMany(group => group.Options).ToArray();
        if (definitions.Groups.Any(group => group.Options.Any(option => option is null || option.Uid == Guid.Empty || option.IsDeleted
                || option.Version <= 0 || option.AttributeGroupId != group.Uid))
            || options.Select(row => row.Uid).Distinct().Count() != options.Length
            || definitions.ProfileRelations.Any(row => !groupIds.Contains(row.AttributeGroupId) || !profileIds.Contains(row.ProductAttributeProfileId))
            || definitions.ProfileRelations.Select(row => row.Uid).Distinct().Count() != definitions.ProfileRelations.Count
            || definitions.ProfileRelations.Select(row => (row.ProductAttributeProfileId, row.AttributeGroupId)).Distinct().Count() != definitions.ProfileRelations.Count)
        {
            error = "属性模板与标签定义的关联不完整，请刷新标签后重试。";
            return false;
        }
        var copy = definitions.Adapt<ProductAttributeDefinitionsDto>();
        copy.Groups = copy.Groups.OrderBy(group => group.SortOrder).ThenBy(group => group.Uid).ToList();
        foreach (var group in copy.Groups)
            group.Options = group.Options.OrderBy(option => option.SortOrder).ThenBy(option => option.Uid).ToList();
        copy.ProfileRelations = copy.ProfileRelations.OrderBy(row => row.ProductAttributeProfileId).ThenBy(row => row.AttributeGroupId).ThenBy(row => row.Uid).ToList();
        prepared = new(typeRows, valueRows, copy);
        error = string.Empty;
        return true;
    }

    /// <summary>复制完整字典快照；内置尚未落库的定义版本可为零，不把读取变成初始化写入。</summary>
    private static bool TryPrepareDictionaryRows(IReadOnlyList<SysDictTypeDto>? types, IReadOnlyList<SysDictDataDto>? values,
        out SysDictTypeDto[] typeRows, out SysDictDataDto[] valueRows)
    {
        typeRows = []; valueRows = [];
        if (types is null || values is null || types.Any(row => row is null || row.Uid == Guid.Empty)
            || values.Any(row => row is null || row.Uid == Guid.Empty)
            || types.Select(row => row.Uid).Distinct().Count() != types.Count
            || values.Select(row => row.Uid).Distinct().Count() != values.Count) return false;
        typeRows = OrderDictionaryTypes(types.Where(row => !row.IsDeleted)).Select(row => row.Adapt<SysDictTypeDto>()).ToArray();
        valueRows = OrderDictionaryValues(values.Where(row => !row.IsDeleted)).Select(row => row.Adapt<SysDictDataDto>()).ToArray();
        return true;
    }

    /// <summary>识别具体模板字典明细，分类和旧类型字符串不参与标签适用性。</summary>
    private static bool IsProductAttributeProfile(SysDictDataDto row) => !row.IsDeleted
        && string.Equals(row.DictCode, DictCodes.ProductAttributeProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>分类刷新只维护分类树展示，不重算标签范围。</summary>
    private static bool TryPrepareCategoryRows(List<ProductCategoryDto>? categories, out ProductCategoryDto[] rows, out string error)
    {
        rows = [];
        error = "产品分类接口没有返回完整快照，请重试。";
        if (categories is null || categories.Any(row => row is null || row.Uid == Guid.Empty)
            || categories.Select(row => row.Uid).Distinct().Count() != categories.Count) return false;
        rows = categories.Where(row => !row.IsDeleted).Select(row => row.Adapt<ProductCategoryDto>()).ToArray();
        error = string.Empty;
        return true;
    }

    /// <summary>接纳完整分类视觉树，与产品属性的模板来源保持独立。</summary>
    private void PublishCategories(ProductCategoryDto[] categories)
    {
        var tree = BuildCategoryTree(categories);
        var flat = FlattenCategoryTree(tree).ToArray();
        _categoriesLoaded = true;
        _productCategoryTree.ReplaceAll(tree);
        _productCategories.ReplaceAll(flat);
    }

    /// <summary>先替换解析快照，再发布字典与标签集合，订阅者读取同一次完整结果。</summary>
    private void PublishProductAttributes(PreparedProductAttributes prepared)
    {
        _attributeSnapshot = prepared.Definitions;
        _profileSnapshot = prepared.DictionaryValues.Where(IsProductAttributeProfile).Select(row => row.Adapt<SysDictDataDto>()).ToArray();
        _attributesLoaded = _dictionariesLoaded = true;
        _dictionaryTypes.ReplaceAll(prepared.DictionaryTypes);
        _dictionaryValues.ReplaceAll(prepared.DictionaryValues);
        _productAttributeGroups.ReplaceAll(prepared.Definitions.Groups);
        _productAttributeProfileRelations.ReplaceAll(prepared.Definitions.ProfileRelations);
    }

    /// <summary>仅在网络响应通过完整性校验后交给 UI 发布，不承载编辑状态。</summary>
    private sealed record PreparedProductAttributes(SysDictTypeDto[] DictionaryTypes, SysDictDataDto[] DictionaryValues,
        ProductAttributeDefinitionsDto Definitions);

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductColorDto>>> LoadColorsAsync() =>
        _apiCallRunner.RunAsync(() => _productColorApi.GetAllAsync(), "加载产品颜色缓存失败");

    /// <summary>
    /// 逐页读取当前用户可见的全部客户，所有分页成功后才返回完整快照。
    /// </summary>
    private Task<ServiceResult<IReadOnlyList<CustomerDto>>> LoadCustomersAsync(CancellationToken cancellationToken = default) =>
        LoadAllPagesAsync(
            (pageIndex, pageSize) => _customerApi.GetPagedAsync(new CustomerSearchRequest
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
            }),
            "加载客户缓存失败", cancellationToken);

    /// <summary>
    /// 逐页读取当前用户可见的全部供应商，所有分页成功后才返回完整快照。
    /// </summary>
    private Task<ServiceResult<IReadOnlyList<SupplierDto>>> LoadSuppliersAsync(CancellationToken cancellationToken = default) =>
        LoadAllPagesAsync(
            (pageIndex, pageSize) => _supplierApi.GetPagedAsync(new SupplierSearchRequest
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
            }),
            "加载供应商缓存失败", cancellationToken);

    /// <summary>逐页读取当前用户可见的全部活动仓库，完整成功后才允许发布快照。</summary>
    private Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>> LoadWarehouseSitesAsync(CancellationToken cancellationToken = default) =>
        LoadAllPagesAsync(
            (pageIndex, pageSize) => _warehouseSiteApi.GetWarehousePickerPagedAsync(new PagedRequestBase
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Keyword = string.Empty
            }, cancellationToken),
            "加载活动仓库缓存失败", cancellationToken);

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductCategoryDto>>> LoadCategoriesAsync() =>
        _apiCallRunner.RunAsync(() => _productCategoryApi.GetAllAsync(), "加载产品分类缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> LoadCurrenciesAsync() =>
        _apiCallRunner.RunAsync(() => _currencyApi.GetEnabledListAsync(), "加载币种缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictTypeDto>>> LoadDictionaryTypesAsync() =>
        _apiCallRunner.RunAsync(() => _dictTypeApi.GetAllAsync(), "加载字典类型缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictDataDto>>> LoadDictionaryValuesAsync() =>
        _apiCallRunner.RunAsync(() => _dictDataApi.GetAllAsync(), "加载字典值缓存失败");

    /// <summary>
    /// 按分页契约组装完整快照；会话取消后停止后续分页，提前空页按失败处理，避免残缺缓存。
    /// </summary>
    private async Task<ServiceResult<IReadOnlyList<T>>> LoadAllPagesAsync<T>(
        Func<int, int, Task<ServiceResult<PagedResponseBase<T>>>> loadPage,
        string failureMessage, CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        var pageIndex = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _apiCallRunner.RunAsync(
                () => loadPage(pageIndex, MasterDataPageSize),
                cancellationToken, failureMessage);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Success)
            {
                return ServiceResult<IReadOnlyList<T>>.Fail(result.Message);
            }

            var page = result.Data;
            if (page is null)
            {
                return ServiceResult<IReadOnlyList<T>>.Fail($"{failureMessage}：服务端未返回分页数据。");
            }

            items.AddRange(page.Items ?? []);
            if (items.Count >= page.TotalCount)
            {
                return ServiceResult<IReadOnlyList<T>>.Ok(items);
            }

            if (page.Items is null || page.Items.Count == 0)
            {
                return ServiceResult<IReadOnlyList<T>>.Fail($"{failureMessage}：分页结果提前结束，已保留旧缓存。");
            }

            pageIndex++;
        }
    }

    private static IEnumerable<CustomerDto> OrderCustomers(IEnumerable<CustomerDto> source) =>
        source
            .OrderBy(item => item.CustomerCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CustomerName, StringComparer.CurrentCulture)
            .ThenBy(item => item.Uid);

    private static IEnumerable<SupplierDto> OrderSuppliers(IEnumerable<SupplierDto> source) =>
        source
            .OrderBy(item => item.SupplierCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SupplierName, StringComparer.CurrentCulture)
            .ThenBy(item => item.Uid);

    private static IEnumerable<WarehouseSitePickerDto> OrderWarehouseSites(IEnumerable<WarehouseSitePickerDto> source) =>
        source
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ThenBy(item => item.Uid);

    private static IEnumerable<SysDictTypeDto> OrderDictionaryTypes(IEnumerable<SysDictTypeDto> source) =>
        source
            .OrderByDescending(item => item.IsSystem)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.DictName, StringComparer.CurrentCulture)
            .ThenBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Uid);

    private static IEnumerable<SysDicCurrencyDto> OrderCurrencies(IEnumerable<SysDicCurrencyDto> source) =>
        source
            .Where(item => item.IsEnabled)
            .OrderByDescending(item => item.IsBase)
            .ThenBy(item => item.CurrencyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Uid);

    private static IEnumerable<SysDictDataDto> OrderDictionaryValues(IEnumerable<SysDictDataDto> source) =>
        source
            .OrderBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SortOrder ?? int.MaxValue)
            .ThenBy(item => item.DataNameZh, StringComparer.CurrentCulture)
            .ThenBy(item => item.DataNameEn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Uid);

    private static string JoinFailureMessages(params string?[] messages) =>
        string.Join(
            Environment.NewLine,
            messages.Where(message => !string.IsNullOrWhiteSpace(message)).Select(message => message!));

    private static IReadOnlyList<ProductCategoryCacheNode> BuildCategoryTree(
        IReadOnlyCollection<ProductCategoryDto> source)
    {
        var orderedDtos = source
            .OrderBy(dto => dto.SortNo)
            .ThenBy(dto => dto.CategoryName, StringComparer.CurrentCulture)
            .ThenBy(dto => dto.Uid)
            .ToList();
        var nodes = orderedDtos.ToDictionary(dto => dto.Uid, dto => new ProductCategoryCacheNode(dto));
        var parentIds = orderedDtos.ToDictionary(dto => dto.Uid, dto => NormalizeParentId(dto, nodes));
        var cycleIds = FindCycleIds(parentIds);
        var roots = new List<ProductCategoryCacheNode>();

        foreach (var dto in orderedDtos)
        {
            var node = nodes[dto.Uid];
            var parentId = parentIds[dto.Uid];
            if (cycleIds.Contains(dto.Uid) || parentId is null)
            {
                roots.Add(node);
            }
            else
            {
                nodes[parentId.Value].AddChild(node);
            }
        }

        ApplyCategoryPaths(roots, string.Empty);

        return roots;
    }

    /// <summary>递归以 FakeName 生成完整分类显示路径，避免英文名称过长。</summary>
    private static void ApplyCategoryPaths(
        IEnumerable<ProductCategoryCacheNode> nodes,
        string parentPath)
    {
        foreach (var node in nodes)
        {
            var path = string.IsNullOrWhiteSpace(parentPath)
                ? node.FakeName
                : $"{parentPath} / {node.FakeName}";
            node.SetCategoryPath(path);
            ApplyCategoryPaths(node.Children, path);
        }
    }

    private static IEnumerable<ProductCategoryCacheNode> FlattenCategoryTree(
        IEnumerable<ProductCategoryCacheNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenCategoryTree(root.Children))
            {
                yield return child;
            }
        }
    }

    private static Guid? NormalizeParentId(
        ProductCategoryDto dto,
        IReadOnlyDictionary<Guid, ProductCategoryCacheNode> nodes)
    {
        if (dto.ParentId is null
            || dto.ParentId == Guid.Empty
            || dto.ParentId == dto.Uid
            || !nodes.ContainsKey(dto.ParentId.Value))
        {
            return null;
        }

        return dto.ParentId;
    }

    private static HashSet<Guid> FindCycleIds(IReadOnlyDictionary<Guid, Guid?> parentIds)
    {
        var cycleIds = new HashSet<Guid>();
        foreach (var startId in parentIds.Keys)
        {
            var path = new List<Guid>();
            var positions = new Dictionary<Guid, int>();
            var currentId = startId;

            while (positions.TryAdd(currentId, path.Count))
            {
                path.Add(currentId);
                var parentId = parentIds[currentId];
                if (parentId is null)
                {
                    break;
                }

                currentId = parentId.Value;
            }

            if (positions.TryGetValue(currentId, out var cycleStart))
            {
                cycleIds.UnionWith(path.Skip(cycleStart));
            }
        }

        return cycleIds;
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e) => Clear();

    private static Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}

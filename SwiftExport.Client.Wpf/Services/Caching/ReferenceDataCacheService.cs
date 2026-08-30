using System.Collections.ObjectModel;
using System.Windows;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Services.Caching;

/// <summary>
/// 基础资料内存缓存实现。服务以 Singleton 注册，生命周期限定为当前登录会话。
/// </summary>
public sealed class ReferenceDataCacheService : IReferenceDataCacheService, IDisposable
{
    private readonly IProductColorApi _productColorApi;
    private readonly IProductCategoryApi _productCategoryApi;
    private readonly ISysDicCurrencyApi _currencyApi;
    private readonly ISysDictTypeApi _dictTypeApi;
    private readonly ISysDictDataApi _dictDataApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly ISessionService _sessionService;
    private readonly CacheObservableCollection<ProductColorDto> _productColors = [];
    private readonly CacheObservableCollection<SysDicCurrencyDto> _currencies = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategoryTree = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategories = [];
    private readonly CacheObservableCollection<SysDictTypeDto> _dictionaryTypes = [];
    private readonly CacheObservableCollection<SysDictDataDto> _dictionaryValues = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _ensureSync = new();
    private Task<CacheRefreshResult>? _ensureTask;
    private volatile bool _colorsLoaded;
    private volatile bool _currenciesLoaded;
    private volatile bool _categoriesLoaded;
    private volatile bool _dictionariesLoaded;
    private long _sessionGeneration;
    private bool _disposed;

    public ReferenceDataCacheService(
        IProductColorApi productColorApi,
        IProductCategoryApi productCategoryApi,
        ISysDicCurrencyApi currencyApi,
        ISysDictTypeApi dictTypeApi,
        ISysDictDataApi dictDataApi,
        IApiCallRunner apiCallRunner,
        ISessionService sessionService)
    {
        _productColorApi = productColorApi;
        _productCategoryApi = productCategoryApi;
        _currencyApi = currencyApi;
        _dictTypeApi = dictTypeApi;
        _dictDataApi = dictDataApi;
        _apiCallRunner = apiCallRunner;
        _sessionService = sessionService;
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>(_productColors);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>(_currencies);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategoryTree);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategories);
        DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>(_dictionaryTypes);
        DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
        _sessionService.SessionEnded += OnSessionEnded;
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }

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
            if (_colorsLoaded && _categoriesLoaded && _currenciesLoaded && _dictionariesLoaded)
            {
                return Task.FromResult(CacheRefreshResult.Ok("缓存已经加载。"));
            }

            return _ensureTask ??= EnsureLoadedCoreAsync();
        }
    }

    /// <inheritdoc />
    public Task<CacheRefreshResult> RefreshAllAsync() => RefreshAllCoreAsync();

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshProductColorsAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var generation = Volatile.Read(ref _sessionGeneration);
            var result = await LoadColorsAsync();
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
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
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshProductCategoriesAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var generation = Volatile.Read(ref _sessionGeneration);
            var result = await LoadCategoriesAsync();
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var tree = BuildCategoryTree(result.Data ?? []);
            var categories = FlattenCategoryTree(tree).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _productCategoryTree.ReplaceAll(tree);
                _productCategories.ReplaceAll(categories);
                _categoriesLoaded = true;
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次分类缓存结果。");
            }

            return CacheRefreshResult.Ok("产品分类缓存已更新。");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshCurrenciesAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var generation = Volatile.Read(ref _sessionGeneration);
            var result = await LoadCurrenciesAsync();
            if (!result.Success)
            {
                return CacheRefreshResult.Fail(result.Message);
            }

            var currencies = OrderCurrencies(result.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
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
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshDictionariesAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var generation = Volatile.Read(ref _sessionGeneration);
            var typesTask = LoadDictionaryTypesAsync();
            var valuesTask = LoadDictionaryValuesAsync();
            await Task.WhenAll(typesTask, valuesTask);

            var typesResult = await typesTask;
            var valuesResult = await valuesTask;
            if (!typesResult.Success || !valuesResult.Success)
            {
                return CacheRefreshResult.Fail(JoinFailureMessages(
                    typesResult.Success ? null : typesResult.Message,
                    valuesResult.Success ? null : valuesResult.Message));
            }

            var types = OrderDictionaryTypes(typesResult.Data ?? []).ToArray();
            var values = OrderDictionaryValues(valuesResult.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _dictionaryTypes.ReplaceAll(types);
                _dictionaryValues.ReplaceAll(values);
                _dictionariesLoaded = true;
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次字典缓存结果。");
            }

            return CacheRefreshResult.Ok("字典类型和字典值缓存已更新。");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        Interlocked.Increment(ref _sessionGeneration);
        RunOnUiThread(() =>
        {
            _productColors.ReplaceAll([]);
            _currencies.ReplaceAll([]);
            _productCategoryTree.ReplaceAll([]);
            _productCategories.ReplaceAll([]);
            _dictionaryTypes.ReplaceAll([]);
            _dictionaryValues.ReplaceAll([]);
        });

        lock (_ensureSync)
        {
            _colorsLoaded = false;
            _currenciesLoaded = false;
            _categoriesLoaded = false;
            _dictionariesLoaded = false;
            _ensureTask = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionService.SessionEnded -= OnSessionEnded;
        _refreshGate.Dispose();
    }

    private async Task<CacheRefreshResult> EnsureLoadedCoreAsync()
    {
        try
        {
            return await RefreshAllCoreAsync();
        }
        finally
        {
            lock (_ensureSync)
            {
                _ensureTask = null;
            }
        }
    }

    private async Task<CacheRefreshResult> RefreshAllCoreAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var generation = Volatile.Read(ref _sessionGeneration);
            var colorsTask = LoadColorsAsync();
            var categoriesTask = LoadCategoriesAsync();
            var currenciesTask = LoadCurrenciesAsync();
            var dictionaryTypesTask = LoadDictionaryTypesAsync();
            var dictionaryValuesTask = LoadDictionaryValuesAsync();
            await Task.WhenAll(colorsTask, categoriesTask, currenciesTask, dictionaryTypesTask, dictionaryValuesTask);

            var colorsResult = await colorsTask;
            var categoriesResult = await categoriesTask;
            var currenciesResult = await currenciesTask;
            var dictionaryTypesResult = await dictionaryTypesTask;
            var dictionaryValuesResult = await dictionaryValuesTask;
            if (!colorsResult.Success
                || !categoriesResult.Success
                || !currenciesResult.Success
                || !dictionaryTypesResult.Success
                || !dictionaryValuesResult.Success)
            {
                return CacheRefreshResult.Fail(JoinFailureMessages(
                    colorsResult.Success ? null : colorsResult.Message,
                    categoriesResult.Success ? null : categoriesResult.Message,
                    currenciesResult.Success ? null : currenciesResult.Message,
                    dictionaryTypesResult.Success ? null : dictionaryTypesResult.Message,
                    dictionaryValuesResult.Success ? null : dictionaryValuesResult.Message));
            }

            var categoryTree = BuildCategoryTree(categoriesResult.Data ?? []);
            var categories = FlattenCategoryTree(categoryTree).ToArray();
            var currencies = OrderCurrencies(currenciesResult.Data ?? []).ToArray();
            var dictionaryTypes = OrderDictionaryTypes(dictionaryTypesResult.Data ?? []).ToArray();
            var dictionaryValues = OrderDictionaryValues(dictionaryValuesResult.Data ?? []).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _productColors.ReplaceAll(colorsResult.Data ?? []);
                _currencies.ReplaceAll(currencies);
                _productCategoryTree.ReplaceAll(categoryTree);
                _productCategories.ReplaceAll(categories);
                _dictionaryTypes.ReplaceAll(dictionaryTypes);
                _dictionaryValues.ReplaceAll(dictionaryValues);
                _colorsLoaded = true;
                _currenciesLoaded = true;
                _categoriesLoaded = true;
                _dictionariesLoaded = true;
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
            }

            return CacheRefreshResult.Ok("产品颜色、产品分类、币种和字典缓存已更新。");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductColorDto>>> LoadColorsAsync() =>
        _apiCallRunner.RunAsync(() => _productColorApi.GetAllAsync(), "加载产品颜色缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductCategoryDto>>> LoadCategoriesAsync() =>
        _apiCallRunner.RunAsync(() => _productCategoryApi.GetAllAsync(), "加载产品分类缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> LoadCurrenciesAsync() =>
        _apiCallRunner.RunAsync(() => _currencyApi.GetEnabledListAsync(), "加载币种缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictTypeDto>>> LoadDictionaryTypesAsync() =>
        _apiCallRunner.RunAsync(() => _dictTypeApi.GetAllAsync(), "加载字典类型缓存失败");

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictDataDto>>> LoadDictionaryValuesAsync() =>
        _apiCallRunner.RunAsync(() => _dictDataApi.GetAllAsync(), "加载字典值缓存失败");

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

    private static void ApplyCategoryPaths(
        IEnumerable<ProductCategoryCacheNode> nodes,
        string parentPath)
    {
        foreach (var node in nodes)
        {
            var path = string.IsNullOrWhiteSpace(parentPath)
                ? node.CategoryName
                : $"{parentPath} / {node.CategoryName}";
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

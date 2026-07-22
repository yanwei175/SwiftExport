using System.Collections.ObjectModel;
using System.Windows;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
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
    private readonly IApiCallRunner _apiCallRunner;
    private readonly ISessionService _sessionService;
    private readonly CacheObservableCollection<ProductColorDto> _productColors = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategoryTree = [];
    private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategories = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _ensureSync = new();
    private Task<CacheRefreshResult>? _ensureTask;
    private volatile bool _colorsLoaded;
    private volatile bool _categoriesLoaded;
    private long _sessionGeneration;
    private bool _disposed;

    public ReferenceDataCacheService(
        IProductColorApi productColorApi,
        IProductCategoryApi productCategoryApi,
        IApiCallRunner apiCallRunner,
        ISessionService sessionService)
    {
        _productColorApi = productColorApi;
        _productCategoryApi = productCategoryApi;
        _apiCallRunner = apiCallRunner;
        _sessionService = sessionService;
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>(_productColors);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategoryTree);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategories);
        _sessionService.SessionEnded += OnSessionEnded;
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }

    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureLoadedAsync()
    {
        lock (_ensureSync)
        {
            if (_colorsLoaded && _categoriesLoaded)
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
    public void Clear()
    {
        Interlocked.Increment(ref _sessionGeneration);
        RunOnUiThread(() =>
        {
            _productColors.ReplaceAll([]);
            _productCategoryTree.ReplaceAll([]);
            _productCategories.ReplaceAll([]);
        });

        lock (_ensureSync)
        {
            _colorsLoaded = false;
            _categoriesLoaded = false;
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
            await Task.WhenAll(colorsTask, categoriesTask);

            var colorsResult = await colorsTask;
            var categoriesResult = await categoriesTask;
            if (!colorsResult.Success || !categoriesResult.Success)
            {
                var messages = new[]
                    {
                        colorsResult.Success ? null : colorsResult.Message,
                        categoriesResult.Success ? null : categoriesResult.Message
                    }
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => message!);
                return CacheRefreshResult.Fail(string.Join(Environment.NewLine, messages));
            }

            var categoryTree = BuildCategoryTree(categoriesResult.Data ?? []);
            var categories = FlattenCategoryTree(categoryTree).ToArray();
            var applied = false;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _sessionGeneration))
                {
                    return;
                }

                _productColors.ReplaceAll(colorsResult.Data ?? []);
                _productCategoryTree.ReplaceAll(categoryTree);
                _productCategories.ReplaceAll(categories);
                _colorsLoaded = true;
                _categoriesLoaded = true;
                applied = true;
            });
            if (!applied)
            {
                return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
            }

            return CacheRefreshResult.Ok("产品颜色和产品分类缓存已更新。");
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

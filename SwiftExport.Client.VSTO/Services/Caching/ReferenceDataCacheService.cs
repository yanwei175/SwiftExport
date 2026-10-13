using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Api;

namespace SwiftExport.Client.VSTO.Services.Caching
{
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
        private readonly ISysDicCurrencyApi _currencyApi;
        private readonly ISysDictTypeApi _dictTypeApi;
        private readonly ISysDictDataApi _dictDataApi;
        private readonly ApiCallRunner _apiCallRunner;
        private readonly SessionService _sessionService;
        private readonly CacheObservableCollection<ProductColorDto> _productColors = new CacheObservableCollection<ProductColorDto>();
        private readonly CacheObservableCollection<CustomerDto> _customers = new CacheObservableCollection<CustomerDto>();
        private readonly CacheObservableCollection<SupplierDto> _suppliers = new CacheObservableCollection<SupplierDto>();
        private readonly CacheObservableCollection<WarehouseSitePickerDto> _warehouseSites = new CacheObservableCollection<WarehouseSitePickerDto>();
        private readonly CacheObservableCollection<SysDicCurrencyDto> _currencies = new CacheObservableCollection<SysDicCurrencyDto>();
        private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategoryTree = new CacheObservableCollection<ProductCategoryCacheNode>();
        private readonly CacheObservableCollection<ProductCategoryCacheNode> _productCategories = new CacheObservableCollection<ProductCategoryCacheNode>();
        private readonly CacheObservableCollection<SysDictTypeDto> _dictionaryTypes = new CacheObservableCollection<SysDictTypeDto>();
        private readonly CacheObservableCollection<SysDictDataDto> _dictionaryValues = new CacheObservableCollection<SysDictDataDto>();
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly object _ensureSync = new object();
        private Task<CacheRefreshResult> _ensureTask;
        private Task<CacheRefreshResult> _warehouseEnsureTask;
        private volatile bool _colorsLoaded;
        private volatile bool _customersLoaded;
        private volatile bool _suppliersLoaded;
        private volatile bool _warehouseSitesLoaded;
        private volatile bool _currenciesLoaded;
        private volatile bool _categoriesLoaded;
        private volatile bool _dictionariesLoaded;
        private long _sessionGeneration;
        private volatile bool _disposed;
        private readonly IUiDispatcher _dispatcher;
        private long _observedVersion;
        private long _clearedGeneration;

        /// <summary>注入当前账号的只读 API 和 Excel STA 调度器；构造期间不请求网络。</summary>
        public ReferenceDataCacheService(
            ICustomerApi customerApi,
            ISupplierApi supplierApi,
            IWarehouseSiteApi warehouseSiteApi,
            IProductColorApi productColorApi,
            IProductCategoryApi productCategoryApi,
            ISysDicCurrencyApi currencyApi,
            ISysDictTypeApi dictTypeApi,
            ISysDictDataApi dictDataApi,
            ApiCallRunner apiCallRunner,
            SessionService sessionService,
            IUiDispatcher dispatcher)
        {
            _customerApi = customerApi;
            _supplierApi = supplierApi;
            _warehouseSiteApi = warehouseSiteApi;
            _productColorApi = productColorApi;
            _productCategoryApi = productCategoryApi;
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
            DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>(_dictionaryTypes);
            DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
            _dispatcher = dispatcher;
            _observedVersion = sessionService.Capture().Version;
            _sessionService.Changed += OnSessionChanged;
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
        public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }

        /// <inheritdoc />
        public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }

        /// <inheritdoc />
        public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true)
        {
            if (string.IsNullOrWhiteSpace(dictCode))
            {
                return new SysDictDataDto[0];
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
                SynchronizeSession();
                if (_disposed || _sessionService.Capture().User == null)
                    return Task.FromResult(CacheRefreshResult.Fail("请先登录有效账号。"));
                if (_colorsLoaded
                    && _customersLoaded
                    && _suppliersLoaded
                    && _categoriesLoaded
                    && _currenciesLoaded
                    && _dictionariesLoaded)
                {
                    return Task.FromResult(CacheRefreshResult.Ok("缓存已经加载。"));
                }

                if (_ensureTask == null || _ensureTask.IsCompleted)
                    _ensureTask = RefreshAllCoreAsync();
                return _ensureTask;
            }
        }

        /// <inheritdoc />
        public Task<CacheRefreshResult> RefreshAllAsync() => RefreshAllCoreAsync();

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshProductColorsAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadColorsAsync();
                if (!result.Success)
                {
                    return CacheRefreshResult.Fail(result.Message);
                }

                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _productColors.ReplaceAll(result.Data ?? new List<ProductColorDto>());
                        _colorsLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                if (!applied)
                {
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次颜色缓存结果。");
                }

                return CacheRefreshResult.Ok("产品颜色缓存已更新。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshCustomersAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadCustomersAsync();
                if (!result.Success)
                {
                    return CacheRefreshResult.Fail(result.Message);
                }

                var customers = OrderCustomers(result.Data ?? new List<CustomerDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _customers.ReplaceAll(customers);
                        _customersLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                return applied
                    ? CacheRefreshResult.Ok("客户缓存已更新。")
                    : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次客户缓存结果。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshSuppliersAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadSuppliersAsync();
                if (!result.Success)
                {
                    return CacheRefreshResult.Fail(result.Message);
                }

                var suppliers = OrderSuppliers(result.Data ?? new List<SupplierDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _suppliers.ReplaceAll(suppliers);
                        _suppliersLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                return applied
                    ? CacheRefreshResult.Ok("供应商缓存已更新。")
                    : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次供应商缓存结果。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync()
        {
            lock (_ensureSync)
            {
                SynchronizeSession();
                if (_disposed || _sessionService.Capture().User == null)
                    return Task.FromResult(CacheRefreshResult.Fail("请先登录有效账号。"));
                if (_warehouseSitesLoaded)
                    return Task.FromResult(CacheRefreshResult.Ok("仓库缓存已经加载。"));

                if (_warehouseEnsureTask != null && !_warehouseEnsureTask.IsCompleted)
                    return _warehouseEnsureTask;

                return _warehouseEnsureTask = RefreshWarehouseSitesAsync();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshWarehouseSitesAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadWarehouseSitesAsync();
                if (!result.Success)
                    return CacheRefreshResult.Fail(result.Message);

                var warehouses = OrderWarehouseSites(result.Data ?? new List<WarehouseSitePickerDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                            return;

                        ClearCollectionsIfNeeded(generation);
                        _warehouseSites.ReplaceAll(warehouses);
                        _warehouseSitesLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                return applied
                    ? CacheRefreshResult.Ok("活动仓库缓存已更新。")
                    : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次仓库缓存结果。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshProductCategoriesAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadCategoriesAsync();
                if (!result.Success)
                {
                    return CacheRefreshResult.Fail(result.Message);
                }

                var tree = BuildCategoryTree(result.Data ?? new List<ProductCategoryDto>());
                var categories = FlattenCategoryTree(tree).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _productCategoryTree.ReplaceAll(tree);
                        _productCategories.ReplaceAll(categories);
                        _categoriesLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                if (!applied)
                {
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次分类缓存结果。");
                }

                return CacheRefreshResult.Ok("产品分类缓存已更新。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshCurrenciesAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var result = await LoadCurrenciesAsync();
                if (!result.Success)
                {
                    return CacheRefreshResult.Fail(result.Message);
                }

                var currencies = OrderCurrencies(result.Data ?? new List<SysDicCurrencyDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _currencies.ReplaceAll(currencies);
                        _currenciesLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                return applied
                    ? CacheRefreshResult.Ok("币种缓存已更新。")
                    : CacheRefreshResult.Fail("登录会话已经变更，已忽略本次币种缓存结果。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<CacheRefreshResult> RefreshDictionariesAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
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

                var types = OrderDictionaryTypes(typesResult.Data ?? new List<SysDictTypeDto>()).ToArray();
                var values = OrderDictionaryValues(valuesResult.Data ?? new List<SysDictDataDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _dictionaryTypes.ReplaceAll(types);
                        _dictionaryValues.ReplaceAll(values);
                        _dictionariesLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                if (!applied)
                {
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次字典缓存结果。");
                }

                return CacheRefreshResult.Ok("字典类型和字典值缓存已更新。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_ensureSync)
            {
                if (_disposed) return;
                InvalidateCore();
            }
        }

        /// <summary>立即作废请求与加载标记，再投递集合清理；不得同步等待 Excel STA。</summary>
        private void InvalidateCore()
        {
            Volatile.Write(ref _observedVersion, _sessionService.Capture().Version);
            var generation = Interlocked.Increment(ref _sessionGeneration);
            _colorsLoaded = _customersLoaded = _suppliersLoaded = _warehouseSitesLoaded = false;
            _currenciesLoaded = _categoriesLoaded = _dictionariesLoaded = false;
            _ensureTask = _warehouseEnsureTask = null;
            // 异步事件不能把异常传播给登录流程；清理失败由统一日志记录。
            _ = ObserveClearAsync(generation);
        }

        /// <summary>清理旧集合，但不覆盖同一代已发布的新快照。</summary>
        private async Task ObserveClearAsync(long generation)
        {
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (_disposed || generation != _sessionGeneration) return;
                        ClearCollectionsIfNeeded(generation);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception exception) { _apiCallRunner.Describe(exception); }
        }

        /// <summary>新会话首次发布前也执行清理，避免排队清理晚于局部刷新时残留旧账号的其他资料。</summary>
        private void ClearCollectionsIfNeeded(long generation)
        {
            if (_clearedGeneration == generation) return;
            _clearedGeneration = generation;
            _productColors.ReplaceAll(new ProductColorDto[0]);
            _customers.ReplaceAll(new CustomerDto[0]);
            _suppliers.ReplaceAll(new SupplierDto[0]);
            _warehouseSites.ReplaceAll(new WarehouseSitePickerDto[0]);
            _currencies.ReplaceAll(new SysDicCurrencyDto[0]);
            _productCategoryTree.ReplaceAll(new ProductCategoryCacheNode[0]);
            _productCategories.ReplaceAll(new ProductCategoryCacheNode[0]);
            _dictionaryTypes.ReplaceAll(new SysDictTypeDto[0]);
            _dictionaryValues.ReplaceAll(new SysDictDataDto[0]);
        }

        /// <summary>释放事件订阅并拒绝迟到结果；进行中的 HTTP 请求由插件连接超时或宿主释放结束。</summary>
        public void Dispose()
        {
            lock (_ensureSync)
            {
                if (_disposed) return;
                _disposed = true;
                Interlocked.Increment(ref _sessionGeneration);
                _sessionService.Changed -= OnSessionChanged;
                _ensureTask = _warehouseEnsureTask = null;
                // 不释放仍被异步请求持有的信号量；未创建 WaitHandle，无需提前销毁内核资源。
            }
        }

        /// <summary>处理提交身份到 Changed 通知之间的窗口，确保旧加载标记不会跨账号复用。</summary>
        private void SynchronizeSession()
        {
            lock (_ensureSync)
            {
                if (!_disposed && _observedVersion != _sessionService.Capture().Version)
                    InvalidateCore();
            }
        }

        /// <summary>同时校验账号版本、手动清理代次及插件生命周期。</summary>
        private bool IsCurrent(long generation, long version)
        {
            var current = _sessionService.Capture();
            return !_disposed && current.User != null && current.Version == version && Volatile.Read(ref _observedVersion) == version
                && generation == Volatile.Read(ref _sessionGeneration);
        }

        /// <summary>账号切换、退出或失效时只清理缓存，不主动加载资料。</summary>
        private void OnSessionChanged(object sender, EventArgs e) { SynchronizeSession(); }

        /// <summary>完整拉取基础资料后统一发布；仓库保持独立按需加载。</summary>
        private async Task<CacheRefreshResult> RefreshAllCoreAsync()
        {
            SynchronizeSession();
            var generation = Volatile.Read(ref _sessionGeneration);
            var version = _sessionService.Capture().Version;
            if (!IsCurrent(generation, version))
                return CacheRefreshResult.Fail("请先登录有效账号，或重新打开页面。");
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(generation, version))
                    return CacheRefreshResult.Fail("缓存已释放或登录会话已改变，请登录后重试。");
                var colorsTask = LoadColorsAsync();
                var customersTask = LoadCustomersAsync();
                var suppliersTask = LoadSuppliersAsync();
                var categoriesTask = LoadCategoriesAsync();
                var currenciesTask = LoadCurrenciesAsync();
                var dictionaryTypesTask = LoadDictionaryTypesAsync();
                var dictionaryValuesTask = LoadDictionaryValuesAsync();
                await Task.WhenAll(
                    colorsTask,
                    customersTask,
                    suppliersTask,
                    categoriesTask,
                    currenciesTask,
                    dictionaryTypesTask,
                    dictionaryValuesTask);

                var colorsResult = await colorsTask;
                var customersResult = await customersTask;
                var suppliersResult = await suppliersTask;
                var categoriesResult = await categoriesTask;
                var currenciesResult = await currenciesTask;
                var dictionaryTypesResult = await dictionaryTypesTask;
                var dictionaryValuesResult = await dictionaryValuesTask;
                if (!colorsResult.Success
                    || !customersResult.Success
                    || !suppliersResult.Success
                    || !categoriesResult.Success
                    || !currenciesResult.Success
                    || !dictionaryTypesResult.Success
                    || !dictionaryValuesResult.Success)
                {
                    return CacheRefreshResult.Fail(JoinFailureMessages(
                        colorsResult.Success ? null : colorsResult.Message,
                        customersResult.Success ? null : customersResult.Message,
                        suppliersResult.Success ? null : suppliersResult.Message,
                        categoriesResult.Success ? null : categoriesResult.Message,
                        currenciesResult.Success ? null : currenciesResult.Message,
                        dictionaryTypesResult.Success ? null : dictionaryTypesResult.Message,
                        dictionaryValuesResult.Success ? null : dictionaryValuesResult.Message));
                }

                var categoryTree = BuildCategoryTree(categoriesResult.Data ?? new List<ProductCategoryDto>());
                var categories = FlattenCategoryTree(categoryTree).ToArray();
                var customers = OrderCustomers(customersResult.Data ?? new List<CustomerDto>()).ToArray();
                var suppliers = OrderSuppliers(suppliersResult.Data ?? new List<SupplierDto>()).ToArray();
                var currencies = OrderCurrencies(currenciesResult.Data ?? new List<SysDicCurrencyDto>()).ToArray();
                var dictionaryTypes = OrderDictionaryTypes(dictionaryTypesResult.Data ?? new List<SysDictTypeDto>()).ToArray();
                var dictionaryValues = OrderDictionaryValues(dictionaryValuesResult.Data ?? new List<SysDictDataDto>()).ToArray();
                var applied = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    lock (_ensureSync)
                    {
                        if (!IsCurrent(generation, version))
                        {
                            return;
                        }

                        ClearCollectionsIfNeeded(generation);
                        _productColors.ReplaceAll(colorsResult.Data ?? new List<ProductColorDto>());
                        _customers.ReplaceAll(customers);
                        _suppliers.ReplaceAll(suppliers);
                        _currencies.ReplaceAll(currencies);
                        _productCategoryTree.ReplaceAll(categoryTree);
                        _productCategories.ReplaceAll(categories);
                        _dictionaryTypes.ReplaceAll(dictionaryTypes);
                        _dictionaryValues.ReplaceAll(dictionaryValues);
                        _colorsLoaded = true;
                        _customersLoaded = true;
                        _suppliersLoaded = true;
                        _currenciesLoaded = true;
                        _categoriesLoaded = true;
                        _dictionariesLoaded = true;
                        applied = true;
                    }
                }).ConfigureAwait(false);
                if (!applied)
                {
                    return CacheRefreshResult.Fail("登录会话已经变更，已忽略本次缓存结果。");
                }

                return CacheRefreshResult.Ok("产品颜色、客户、供应商、产品分类、币种和字典缓存已更新。");
            }
            catch (Exception exception)
            {
                return CacheRefreshResult.Fail(_apiCallRunner.Describe(exception));
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>读取完整颜色候选。</summary>
        private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductColorDto>>> LoadColorsAsync() =>
            RunApiAsync(() => _productColorApi.GetAllAsync(), "加载产品颜色缓存失败");

        /// <summary>
        /// 逐页读取当前用户可见的全部客户，所有分页成功后才返回完整快照。
        /// </summary>
        private Task<ServiceResult<IReadOnlyList<CustomerDto>>> LoadCustomersAsync() =>
            LoadAllPagesAsync(
                (pageIndex, pageSize) => _customerApi.GetPagedAsync(new CustomerSearchRequest
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                }),
                "加载客户缓存失败");

        /// <summary>
        /// 逐页读取当前用户可见的全部供应商，所有分页成功后才返回完整快照。
        /// </summary>
        private Task<ServiceResult<IReadOnlyList<SupplierDto>>> LoadSuppliersAsync() =>
            LoadAllPagesAsync(
                (pageIndex, pageSize) => _supplierApi.GetPagedAsync(new SupplierSearchRequest
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                }),
                "加载供应商缓存失败");

        /// <summary>逐页读取当前用户可见的全部活动仓库，完整成功后才允许发布快照。</summary>
        private Task<ServiceResult<IReadOnlyList<WarehouseSitePickerDto>>> LoadWarehouseSitesAsync() =>
            LoadAllPagesAsync(
                (pageIndex, pageSize) => _warehouseSiteApi.GetWarehousePickerPagedAsync(new PagedRequestBase
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    Keyword = string.Empty
                }),
                "加载活动仓库缓存失败");

        /// <summary>读取分类数据供本地构建树。</summary>
        private Task<SwiftExport.Contracts.Wrappers.ServiceResult<List<ProductCategoryDto>>> LoadCategoriesAsync() =>
            RunApiAsync(() => _productCategoryApi.GetAllAsync(), "加载产品分类缓存失败");

        /// <summary>读取启用币种。</summary>
        private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> LoadCurrenciesAsync() =>
            RunApiAsync(() => _currencyApi.GetEnabledListAsync(), "加载币种缓存失败");

        /// <summary>读取字典类型。</summary>
        private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictTypeDto>>> LoadDictionaryTypesAsync() =>
            RunApiAsync(() => _dictTypeApi.GetAllAsync(), "加载字典类型缓存失败");

        /// <summary>读取包含停用项的字典值。</summary>
        private Task<SwiftExport.Contracts.Wrappers.ServiceResult<IReadOnlyList<SysDictDataDto>>> LoadDictionaryValuesAsync() =>
            RunApiAsync(() => _dictDataApi.GetAllAsync(), "加载字典值缓存失败");

        /// <summary>
        /// 按分页契约组装完整快照；服务端提前返回空页时按失败处理，避免发布残缺缓存。
        /// </summary>
        private async Task<ServiceResult<IReadOnlyList<T>>> LoadAllPagesAsync<T>(
            Func<int, int, Task<ServiceResult<PagedResponseBase<T>>>> loadPage,
            string failureMessage)
        {
            var version = _sessionService.Capture().Version;
            var generation = Volatile.Read(ref _sessionGeneration);
            var items = new List<T>();
            var pageIndex = 1;
            while (true)
            {
                if (!IsCurrent(generation, version))
                    return ServiceResult<IReadOnlyList<T>>.Fail("登录会话已经改变，已停止分页读取。");
                var result = await RunApiAsync(
                    () => loadPage(pageIndex, MasterDataPageSize),
                    failureMessage);
                if (!result.Success)
                {
                    return ServiceResult<IReadOnlyList<T>>.Fail(result.Message);
                }

                var page = result.Data;
                if (page is null)
                {
                    return ServiceResult<IReadOnlyList<T>>.Fail($"{failureMessage}：服务端未返回分页数据。");
                }

                items.AddRange(page.Items ?? new List<T>());
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

        /// <summary>按客户代码、名称和主键稳定排序。</summary>
        private static IEnumerable<CustomerDto> OrderCustomers(IEnumerable<CustomerDto> source) =>
            source
                .OrderBy(item => item.CustomerCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.CustomerName, StringComparer.CurrentCulture)
                .ThenBy(item => item.Uid);

        /// <summary>按供应商代码、名称和主键稳定排序。</summary>
        private static IEnumerable<SupplierDto> OrderSuppliers(IEnumerable<SupplierDto> source) =>
            source
                .OrderBy(item => item.SupplierCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SupplierName, StringComparer.CurrentCulture)
                .ThenBy(item => item.Uid);

        /// <summary>按仓库代码、名称和主键稳定排序。</summary>
        private static IEnumerable<WarehouseSitePickerDto> OrderWarehouseSites(IEnumerable<WarehouseSitePickerDto> source) =>
            source
                .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.CurrentCulture)
                .ThenBy(item => item.Uid);

        /// <summary>按系统标记和业务排序展示字典类型。</summary>
        private static IEnumerable<SysDictTypeDto> OrderDictionaryTypes(IEnumerable<SysDictTypeDto> source) =>
            source
                .OrderByDescending(item => item.IsSystem)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.DictName, StringComparer.CurrentCulture)
                .ThenBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Uid);

        /// <summary>仅展示启用币种并优先显示本位币。</summary>
        private static IEnumerable<SysDicCurrencyDto> OrderCurrencies(IEnumerable<SysDicCurrencyDto> source) =>
            source
                .Where(item => item.IsEnabled)
                .OrderByDescending(item => item.IsBase)
                .ThenBy(item => item.CurrencyCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Uid);

        /// <summary>按字典编码和业务顺序稳定排序。</summary>
        private static IEnumerable<SysDictDataDto> OrderDictionaryValues(IEnumerable<SysDictDataDto> source) =>
            source
                .OrderBy(item => item.DictCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SortOrder ?? int.MaxValue)
                .ThenBy(item => item.DataNameZh, StringComparer.CurrentCulture)
                .ThenBy(item => item.DataNameEn, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Uid);

        /// <summary>合并非空失败提示。</summary>
        private static string JoinFailureMessages(params string[] messages) =>
            string.Join(
                Environment.NewLine,
                messages.Where(message => !string.IsNullOrWhiteSpace(message)).Select(message => message));

        /// <summary>构建分类树；孤立、自引用和循环节点提升为根。</summary>
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

        /// <summary>按树顺序复用节点生成扁平候选。</summary>
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

        /// <summary>排除缺失、自引用和不存在的父节点。</summary>
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

        /// <summary>识别父链循环，防止分类树递归不终止。</summary>
        private static HashSet<Guid> FindCycleIds(IReadOnlyDictionary<Guid, Guid?> parentIds)
        {
            var cycleIds = new HashSet<Guid>();
            foreach (var startId in parentIds.Keys)
            {
                var path = new List<Guid>();
                var positions = new Dictionary<Guid, int>();
                var currentId = startId;

                while (!positions.ContainsKey(currentId))
                {
                    positions.Add(currentId, path.Count);
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

        /// <summary>将网络异常归一为可显示失败，失败或空响应不能覆盖旧快照。</summary>
        private async Task<ServiceResult<T>> RunApiAsync<T>(Func<Task<ServiceResult<T>>> request, string failureMessage)
        {
            try
            {
                var result = await request().ConfigureAwait(false);
                if (result == null || (result.Success && ReferenceEquals(result.Data, null)))
                    return ServiceResult<T>.Fail(failureMessage + "：服务端未返回数据。");
                return result;
            }
            catch (Exception exception)
            {
                return ServiceResult<T>.Fail(failureMessage + "：" + _apiCallRunner.Describe(exception));
            }
        }
    }
}

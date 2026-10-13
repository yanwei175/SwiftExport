using System.Collections.ObjectModel;
using System.Reflection;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
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
    private readonly ObservableCollection<ProductAttributeGroupDto> _productAttributeGroups;
    private readonly ObservableCollection<ProductCategoryAttributeGroupDto> _productAttributeCategoryRelations;
    private readonly ObservableCollection<ProductCategoryCacheNode> _productCategories = [];
    private readonly ObservableCollection<ProductCategoryCacheNode> _productCategoryTree = [];
    private IReadOnlyDictionary<Guid, Guid?> _categoryParents = new Dictionary<Guid, Guid?>();
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
        IEnumerable<CustomerDto>? customers = null,
        IEnumerable<ProductAttributeGroupDto>? productAttributeGroups = null,
        IEnumerable<ProductCategoryAttributeGroupDto>? productAttributeCategoryRelations = null,
        IEnumerable<ProductCategoryDto>? productCategories = null)
    {
        _dictionaryValues = new ObservableCollection<SysDictDataDto>(dictionaryValues ?? []);
        _customers = new ObservableCollection<CustomerDto>(customers ?? []);
        _suppliers = new ObservableCollection<SupplierDto>(suppliers ?? []);
        _warehouseSites = new ObservableCollection<WarehouseSitePickerDto>(warehouseSites ?? []);
        _productAttributeGroups = new ObservableCollection<ProductAttributeGroupDto>(productAttributeGroups ?? []);
        _productAttributeCategoryRelations = new ObservableCollection<ProductCategoryAttributeGroupDto>(productAttributeCategoryRelations ?? []);
        _ensureResult = CreateRefreshResult(ensureSuccess, ensureMessage);
        _supplierRefreshResult = CreateRefreshResult(supplierRefreshSuccess, supplierRefreshMessage);
        _warehouseRefreshResult = CreateRefreshResult(warehouseRefreshSuccess, warehouseRefreshMessage);
        Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>([]);
        Customers = new ReadOnlyObservableCollection<CustomerDto>(_customers);
        Suppliers = new ReadOnlyObservableCollection<SupplierDto>(_suppliers);
        WarehouseSites = new ReadOnlyObservableCollection<WarehouseSitePickerDto>(_warehouseSites);
        ProductColors = new ReadOnlyObservableCollection<ProductColorDto>([]);
        ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategoryTree);
        ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_productCategories);
        ProductAttributeGroups = new ReadOnlyObservableCollection<ProductAttributeGroupDto>(_productAttributeGroups);
        ProductAttributeCategoryRelations = new ReadOnlyObservableCollection<ProductCategoryAttributeGroupDto>(_productAttributeCategoryRelations);
        DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>([]);
        DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
        SetProductCategoriesCore(productCategories ?? []);
    }

    public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }
    public ReadOnlyObservableCollection<CustomerDto> Customers { get; }
    public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }
    public ReadOnlyObservableCollection<WarehouseSitePickerDto> WarehouseSites { get; }
    public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }
    public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }
    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductAttributeGroupDto> ProductAttributeGroups { get; }
    /// <inheritdoc />
    public ReadOnlyObservableCollection<ProductCategoryAttributeGroupDto> ProductAttributeCategoryRelations { get; }
    /// <inheritdoc />
    public bool AreProductAttributesLoaded { get; private set; } = true;
    /// <inheritdoc />
    public event EventHandler? ProductAttributesChanged;
    /// <summary>控制离线标签独立刷新是否成功，供失败保留草稿测试使用。</summary>
    public bool ProductAttributesRefreshSuccess { get; set; } = true;
    /// <summary>记录标签请求入口次数，验证本地选择不重复拉取网络资料。</summary>
    public int EnsureProductAttributesLoadedCallCount { get; private set; }
    /// <summary>记录标签独立刷新次数。</summary>
    public int RefreshProductAttributesCallCount { get; private set; }
    /// <inheritdoc />
    public ProductAttributeResolution ResolveProductAttributes(Guid categoryId) => AreProductAttributesLoaded
        ? ProductAttributeInheritance.Resolve(categoryId, _categoryParents, ProductAttributeGroups, ProductAttributeCategoryRelations)
        : new ProductAttributeResolution(false, "测试标签快照尚未加载。", [], []);
    /// <inheritdoc />
    public IReadOnlyList<ProductAttributeGroupDto> GetProductAttributeGroups(Guid categoryId)
    {
        var result = ResolveProductAttributes(categoryId);
        return result.Success ? result.Groups : throw new InvalidOperationException(result.Message);
    }
    /// <inheritdoc />
    public IReadOnlyList<ProductAttributeOptionDto> GetProductAttributeOptions(Guid groupId) =>
        ProductAttributeGroups.FirstOrDefault(group => group.Uid == groupId)?.Options.ToArray() ?? [];
    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureProductAttributesLoadedAsync()
    {
        EnsureProductAttributesLoadedCallCount++;
        AreProductAttributesLoaded = _ensureResult.Success;
        return Task.FromResult(_ensureResult);
    }
    /// <inheritdoc />
    public Task<CacheRefreshResult> RefreshProductAttributesAsync()
    {
        RefreshProductAttributesCallCount++;
        if (ProductAttributesRefreshSuccess)
        {
            AreProductAttributesLoaded = true;
            ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
        }
        return Task.FromResult(CreateRefreshResult(ProductAttributesRefreshSuccess, "测试标签缓存刷新结果。"));
    }
    /// <summary>替换离线标签定义并通知订阅者，模拟服务端定义刷新且不包含产品选择状态。</summary>
    public void SetProductAttributeGroups(IEnumerable<ProductAttributeGroupDto> groups)
        => SetProductAttributeSnapshot(groups, ProductAttributeCategoryRelations.ToArray());

    /// <summary>同时替换离线组、直接关联和可选分类链，发布后才通知消费页面。</summary>
    public void SetProductAttributeSnapshot(IEnumerable<ProductAttributeGroupDto> groups,
        IEnumerable<ProductCategoryAttributeGroupDto> relations, IEnumerable<ProductCategoryDto>? categories = null)
    {
        var groupRows = groups.ToArray();
        var relationRows = relations.ToArray();
        _productAttributeGroups.Clear();
        foreach (var group in groupRows)
            _productAttributeGroups.Add(group);
        _productAttributeCategoryRelations.Clear();
        foreach (var relation in relationRows)
            _productAttributeCategoryRelations.Add(relation);
        if (categories is not null) SetProductCategoriesCore(categories);
        AreProductAttributesLoaded = true;
        ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>替换测试分类原始父链并通知标签消费方，模拟分类刷新。</summary>
    public void SetProductCategories(IEnumerable<ProductCategoryDto> categories)
    {
        SetProductCategoriesCore(categories);
        ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>准备离线分类候选与原始父链；测试节点不包含编辑状态。</summary>
    private void SetProductCategoriesCore(IEnumerable<ProductCategoryDto> categories)
    {
        var rows = categories.Where(row => !row.IsDeleted).ToArray();
        _categoryParents = rows.ToDictionary(row => row.Uid, row => row.ParentId);
        _productCategories.Clear();
        _productCategoryTree.Clear();
        foreach (var row in rows)
        {
            var node = new ProductCategoryCacheNode(row);
            node.SetCategoryPath(row.CategoryName);
            _productCategories.Add(node);
            if (row.ParentId is null || row.ParentId == Guid.Empty) _productCategoryTree.Add(node);
        }
    }
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
        _productAttributeGroups.Clear();
        _productAttributeCategoryRelations.Clear();
        _productCategories.Clear();
        _productCategoryTree.Clear();
        _categoryParents = new Dictionary<Guid, Guid?>();
        AreProductAttributesLoaded = false;
        ProductAttributesChanged?.Invoke(this, EventArgs.Empty);
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

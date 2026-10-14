using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;

namespace SwiftExportClient.Services.Caching;

/// <summary>
/// 应用级基础资料缓存。通过依赖注入获取，不要在 ViewModel 中自行创建实例。
/// </summary>
public interface IReferenceDataCacheService
{
    /// <summary>
    /// 获取全部启用币种的稳定只读缓存。币种来自独立的 SysDicCurrency 主数据。
    /// </summary>
    ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }

    /// <summary>
    /// 获取产品颜色只读缓存。集合可直接绑定控件；调用方不得修改其中的 DTO。
    /// </summary>
    ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }

    /// <summary>
    /// 获取当前登录会话可见的客户只读缓存。集合可直接绑定本地搜索控件。
    /// </summary>
    ReadOnlyObservableCollection<CustomerDto> Customers { get; }

    /// <summary>
    /// 获取当前登录会话可见的供应商只读缓存。集合可直接绑定本地搜索控件。
    /// </summary>
    ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }

    /// <summary>
    /// 获取当前登录会话可见的活动仓库选择候选；集合仅供本地筛选，调用方不得修改其中 DTO。
    /// </summary>
    ReadOnlyObservableCollection<WarehouseSitePickerDto> WarehouseSites { get; }

    /// <summary>
    /// 获取产品分类只读树，可直接绑定 TreeView.ItemsSource。
    /// </summary>
    ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }

    /// <summary>
    /// 获取按树顺序扁平化的产品分类只读缓存，供本地 Lookup 使用。
    /// 集合中的节点与 <see cref="ProductCategoryTree"/> 使用同一对象实例。
    /// </summary>
    ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }

    /// <summary>当前会话的全局标签定义，包含未关联模板的组及其选项；调用方不得编辑 DTO 或保存勾选状态。</summary>
    ReadOnlyObservableCollection<ProductAttributeGroupDto> ProductAttributeGroups { get; }

    /// <summary>有效属性模板与组的直接关联；调用方不得修改 DTO。</summary>
    ReadOnlyObservableCollection<ProductAttributeProfileGroupDto> ProductAttributeProfileRelations { get; }

    /// <summary>模板字典与全局标签是否均已完整加载；成功的空快照也视为已加载。</summary>
    bool AreProductAttributesLoaded { get; }

    /// <summary>完整标签快照发布或会话清空后触发；订阅者应在释放时取消订阅。</summary>
    event EventHandler? ProductAttributesChanged;

    /// <summary>按字典模板解析直接关联组，模板缺失、停用或未完整加载均显式失败。</summary>
    ProductAttributeResolution ResolveProductAttributes(Guid profileId);

    /// <summary>读取属性模板直接关联的标签组；解析失败抛异常，须保留选择的编辑页应使用显式解析结果。</summary>
    IReadOnlyList<ProductAttributeGroupDto> GetProductAttributeGroups(Guid profileId);

    /// <summary>按属性组读取本地选项，不调用 API。</summary>
    IReadOnlyList<ProductAttributeOptionDto> GetProductAttributeOptions(Guid groupId);

    /// <summary>首次使用时加载完整标签快照，合并并行请求，失败后允许重试。</summary>
    Task<CacheRefreshResult> EnsureProductAttributesLoadedAsync();

    /// <summary>同时刷新字典与完整标签快照；任一失败保留两者旧数据，旧会话结果不得回写。</summary>
    Task<CacheRefreshResult> RefreshProductAttributesAsync();

    /// <summary>
    /// 获取全部未删除字典类型的只读缓存。
    /// </summary>
    ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }

    /// <summary>
    /// 获取全部未删除字典值的只读缓存，包含停用值。
    /// </summary>
    ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }

    /// <summary>
    /// 按字典编码读取字典值。业务下拉默认只返回启用值。
    /// </summary>
    IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true);

    /// <summary>
    /// 首次使用时加载基础资料和标签；标签失败不阻断其他基础资料，标签状态需独立检查。
    /// </summary>
    Task<CacheRefreshResult> EnsureLoadedAsync();

    /// <summary>
    /// 强制刷新基础资料及独立标签快照。基础资料失败保留原有完整快照，标签状态独立发布。
    /// </summary>
    Task<CacheRefreshResult> RefreshAllAsync();

    /// <summary>
    /// 强制重新拉取产品颜色缓存；失败时保留旧颜色缓存。
    /// </summary>
    Task<CacheRefreshResult> RefreshProductColorsAsync();

    /// <summary>
    /// 强制重新拉取完整客户缓存；任一分页请求失败时保留旧客户缓存。
    /// </summary>
    Task<CacheRefreshResult> RefreshCustomersAsync();

    /// <summary>
    /// 强制重新拉取完整供应商缓存；任一分页请求失败时保留旧供应商缓存。
    /// </summary>
    Task<CacheRefreshResult> RefreshSuppliersAsync();

    /// <summary>
    /// 首次使用仓库选择器时独立加载完整活动仓库缓存，不影响其他基础资料缓存初始化。
    /// </summary>
    Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync();

    /// <summary>
    /// 强制逐页刷新完整活动仓库缓存；任一分页失败时保留旧仓库快照。
    /// </summary>
    Task<CacheRefreshResult> RefreshWarehouseSitesAsync();

    /// <summary>
    /// 强制重新拉取产品分类缓存；失败时保留旧分类树。
    /// </summary>
    Task<CacheRefreshResult> RefreshProductCategoriesAsync();

    /// <summary>
    /// 强制刷新启用币种缓存；失败时保留旧币种快照。
    /// </summary>
    Task<CacheRefreshResult> RefreshCurrenciesAsync();

    /// <summary>
    /// 强制原子刷新字典类型和字典值；任一请求失败时保留旧字典快照。
    /// </summary>
    Task<CacheRefreshResult> RefreshDictionariesAsync();

    /// <summary>
    /// 清除当前登录会话的内存缓存。正常情况下由会话结束事件自动调用。
    /// </summary>
    void Clear();
}

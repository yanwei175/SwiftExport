using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Services.Caching;

/// <summary>
/// 应用级基础资料缓存。通过依赖注入获取，不要在 ViewModel 中自行创建实例。
/// </summary>
public interface IReferenceDataCacheService
{
    /// <summary>
    /// 获取产品颜色只读缓存。集合可直接绑定控件；调用方不得修改其中的 DTO。
    /// </summary>
    ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }

    /// <summary>
    /// 获取产品分类只读树，可直接绑定 TreeView.ItemsSource。
    /// </summary>
    ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }

    /// <summary>
    /// 获取按树顺序扁平化的产品分类只读缓存，供本地 Lookup 使用。
    /// 集合中的节点与 <see cref="ProductCategoryTree"/> 使用同一对象实例。
    /// </summary>
    ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }

    /// <summary>
    /// 首次使用时加载全部已接入缓存；本会话已完整加载时不会重复请求。
    /// </summary>
    Task<CacheRefreshResult> EnsureLoadedAsync();

    /// <summary>
    /// 强制重新拉取全部已接入缓存。任一请求失败时保留原有完整快照。
    /// </summary>
    Task<CacheRefreshResult> RefreshAllAsync();

    /// <summary>
    /// 强制重新拉取产品颜色缓存；失败时保留旧颜色缓存。
    /// </summary>
    Task<CacheRefreshResult> RefreshProductColorsAsync();

    /// <summary>
    /// 强制重新拉取产品分类缓存；失败时保留旧分类树。
    /// </summary>
    Task<CacheRefreshResult> RefreshProductCategoriesAsync();

    /// <summary>
    /// 清除当前登录会话的内存缓存。正常情况下由会话结束事件自动调用。
    /// </summary>
    void Clear();
}

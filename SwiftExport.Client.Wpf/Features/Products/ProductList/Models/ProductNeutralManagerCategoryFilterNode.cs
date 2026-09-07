using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>中性产品管理页只读分类筛选节点，区分全部、未分类和真实分类。</summary>
public sealed class ProductNeutralManagerCategoryFilterNode
{
    private ProductNeutralManagerCategoryFilterNode(
        string displayName,
        ProductCategoryCacheNode? source,
        bool isAll,
        bool isUncategorized)
    {
        DisplayName = displayName;
        Source = source;
        IsAll = isAll;
        IsUncategorized = isUncategorized;
        Children = source?.Children.Select(child => FromCategory(child)).ToArray()
            ?? Array.Empty<ProductNeutralManagerCategoryFilterNode>();
    }

    /// <summary>创建“全部分类”特殊节点。</summary>
    public static ProductNeutralManagerCategoryFilterNode All() => new("全部分类", null, true, false);

    /// <summary>创建“未分类”特殊节点。</summary>
    public static ProductNeutralManagerCategoryFilterNode Uncategorized() => new("未分类", null, false, true);

    /// <summary>从缓存分类及其子树创建筛选节点。</summary>
    public static ProductNeutralManagerCategoryFilterNode FromCategory(ProductCategoryCacheNode source) =>
        new(source.CategoryName, source, false, false);

    /// <summary>获取节点显示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取真实分类来源；特殊节点为空。</summary>
    public ProductCategoryCacheNode? Source { get; }
    /// <summary>获取是否代表全部分类。</summary>
    public bool IsAll { get; }
    /// <summary>获取是否代表未分类。</summary>
    public bool IsUncategorized { get; }
    /// <summary>获取只读子分类节点。</summary>
    public IReadOnlyList<ProductNeutralManagerCategoryFilterNode> Children { get; }
}

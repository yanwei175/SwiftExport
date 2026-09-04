using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号管理页的只读分类筛选节点，包装缓存节点并提供“全部分类”。</summary>
public sealed class ProductMasterCategoryFilterNode
{
    /// <summary>初始化分类筛选节点。</summary>
    public ProductMasterCategoryFilterNode(string displayName, ProductCategoryCacheNode? source = null)
    {
        DisplayName = displayName;
        Source = source;
        Children = source?.Children.Select(child => new ProductMasterCategoryFilterNode(child.CategoryName, child)).ToArray()
            ?? Array.Empty<ProductMasterCategoryFilterNode>();
    }

    /// <summary>获取显示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取对应缓存分类；空值表示全部分类。</summary>
    public ProductCategoryCacheNode? Source { get; }
    /// <summary>获取只读子节点。</summary>
    public IReadOnlyList<ProductMasterCategoryFilterNode> Children { get; }
    /// <summary>获取分类标识；全部分类返回空值。</summary>
    public Guid? CategoryId => Source?.Uid;
}

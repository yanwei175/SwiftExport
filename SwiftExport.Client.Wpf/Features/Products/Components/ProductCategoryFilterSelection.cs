namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 表示产品分类筛选树的一次不可变选择快照。
/// </summary>
public sealed record ProductCategoryFilterSelection
{
    /// <summary>创建分类筛选快照，并复制分类标识以隔离调用方后续修改。</summary>
    /// <param name="isAll">是否不限制产品分类。</param>
    /// <param name="includeUncategorized">是否包含未设置分类的数据。</param>
    /// <param name="categoryIds">按分类树显示顺序排列的分类标识。</param>
    public ProductCategoryFilterSelection(bool isAll, bool includeUncategorized, IReadOnlyList<Guid>? categoryIds)
    {
        IsAll = isAll;
        IncludeUncategorized = includeUncategorized;
        CategoryIds = categoryIds?.ToArray() ?? Array.Empty<Guid>();
    }

    /// <summary>获取表示“不限制产品分类”的默认选择。</summary>
    public static ProductCategoryFilterSelection AllCategories { get; } =
        new(true, false, Array.Empty<Guid>());

    /// <summary>获取是否不限制产品分类。</summary>
    public bool IsAll { get; }

    /// <summary>获取是否包含未设置分类的数据。</summary>
    public bool IncludeUncategorized { get; }

    /// <summary>获取按分类树显示顺序排列的分类标识副本。</summary>
    public IReadOnlyList<Guid> CategoryIds { get; }
}

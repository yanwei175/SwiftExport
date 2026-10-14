using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 表示产品分类筛选的一次不可变选择快照，并提供仅用于本地选择的未分类选项。
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

    /// <summary>未分类的只读本地选项；空身份仅表示筛选语义，不写入共享缓存或作为真实分类 ID 发往 API。</summary>
    public static ProductCategoryCacheNode UncategorizedOption { get; } = new(new ProductCategoryDto
    {
        Uid = Guid.Empty,
        FakeName = "未分类"
    });

    /// <summary>获取是否不限制产品分类。</summary>
    public bool IsAll { get; }

    /// <summary>获取是否包含未设置分类的数据。</summary>
    public bool IncludeUncategorized { get; }

    /// <summary>获取按分类树显示顺序排列的分类标识副本。</summary>
    public IReadOnlyList<Guid> CategoryIds { get; }
}

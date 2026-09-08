namespace SwiftExportClient.Features.Products.Components;

/// <summary>表示产品颜色筛选控件的一次不可变选择快照；空集合表示全部颜色。</summary>
public sealed record ProductColorFilterSelection
{
    /// <summary>创建颜色筛选快照，并按输入顺序去重及排除空标识。</summary>
    /// <param name="colorIds">选中的颜色标识。</param>
    public ProductColorFilterSelection(IReadOnlyList<Guid>? colorIds)
    {
        ColorIds = colorIds?.Where(id => id != Guid.Empty).Distinct().ToArray()
            ?? Array.Empty<Guid>();
    }

    /// <summary>获取表示“不限制颜色”的默认选择。</summary>
    public static ProductColorFilterSelection AllColors { get; } = new(Array.Empty<Guid>());

    /// <summary>获取按颜色缓存顺序排列的标识副本。</summary>
    public IReadOnlyList<Guid> ColorIds { get; }
}

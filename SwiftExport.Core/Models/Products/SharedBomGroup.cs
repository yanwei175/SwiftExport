using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Models.Products;

/// <summary>同一数据库等值编号下的物理组成与引用产品；不以来源父产品或标记数值区分组成版本。</summary>
public sealed class SharedBomGroup
{
    /// <summary>仅本次读取内有效的组标识，禁止持久化或跨次查询比较。</summary>
    public long GroupKey { get; init; }

    /// <summary>组内原始编号的代表文本；不能用于重新推断数据库等值关系。</summary>
    public string BomNo { get; init; } = string.Empty;

    /// <summary>属于本组的请求编号下标，从零开始，保留重复输入。</summary>
    public IReadOnlyList<int> InputIndexes { get; init; } = [];

    /// <summary>每个未删物理组成行仅一次，保留异常标记供业务明确校验。</summary>
    public IReadOnlyList<Bom> Lines { get; init; } = [];

    /// <summary>未删除且有效标记为正的引用产品；空组成也保留产品引用。</summary>
    public IReadOnlyList<Product> Products { get; init; } = [];
}

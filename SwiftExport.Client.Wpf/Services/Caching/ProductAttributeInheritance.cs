using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExportClient.Services.Caching;

/// <summary>使用原始父链解析共享标签；视觉分类树的异常节点提升规则不参与适用性判断。</summary>
public static class ProductAttributeInheritance
{
    /// <summary>按自身到根的分类链合并直接关联；缺失或循环返回失败，正常无关联返回成功空集合。</summary>
    public static ProductAttributeResolution Resolve(Guid categoryId, IReadOnlyDictionary<Guid, Guid?> parents,
        IEnumerable<ProductAttributeGroupDto> groups, IEnumerable<ProductCategoryAttributeGroupDto> relations)
    {
        if (categoryId == Guid.Empty)
            return new(false, "请选择有效分类后再维护标签。", [], []);
        var ancestors = new HashSet<Guid>();
        var current = categoryId;
        while (current != Guid.Empty)
        {
            if (!ancestors.Add(current))
                return new(false, "分类层级存在循环，无法解析标签，请刷新或修正分类。", [], []);
            if (!parents.TryGetValue(current, out var parent))
                return new(false, "分类或祖先不存在、已删除，无法解析标签，请刷新分类和标签。", [], []);
            current = parent ?? Guid.Empty;
        }
        var sources = relations.Where(row => ancestors.Contains(row.CategoryId))
            .OrderBy(row => row.CategoryId).ThenBy(row => row.Uid).ToArray();
        var groupIds = sources.Select(row => row.AttributeGroupId).ToHashSet();
        var available = groups.Where(row => groupIds.Contains(row.Uid))
            .OrderBy(row => row.SortOrder).ThenBy(row => row.Uid).ToArray();
        if (available.Length != groupIds.Count)
            return new(false, "分类关联的标签组缺失，请刷新标签后重试。", [], []);
        return new(true, string.Empty, available, sources);
    }
}

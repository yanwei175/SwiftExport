using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExport.Application.Services.Products.Attributes;

/// <summary>按分类祖先路径解析共享标签适用范围；仅计算最终图，不读取数据库或修改选择。</summary>
public static class ProductAttributeApplicability
{
    /// <summary>返回含自身的有效祖先路径；缺失节点或循环视为分类数据错误，不返回不完整结果。</summary>
    public static IReadOnlyList<Guid> GetAncestorIds(Guid categoryId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var path = new List<Guid>();
        var visited = new HashSet<Guid>();
        var current = categoryId;
        while (current != Guid.Empty)
        {
            if (!visited.Add(current)) throw new InvalidOperationException("产品分类层级存在循环，请修正分类后重试。");
            if (!parents.TryGetValue(current, out var parent)) throw new InvalidOperationException("产品分类或祖先不存在、已删除，请刷新分类后重试。");
            path.Add(current);
            current = parent ?? Guid.Empty;
        }
        if (path.Count == 0) throw new InvalidOperationException("请选择有效的产品分类。");
        return path;
    }

    /// <summary>合并自身及祖先的直接关联，按组身份去重；不向后代复制关系。</summary>
    public static IReadOnlySet<Guid> GetApplicableGroupIds(Guid categoryId, IReadOnlyDictionary<Guid, Guid?> parents,
        IEnumerable<ProductCategoryAttributeGroupDto> relations)
    {
        var ancestors = GetAncestorIds(categoryId, parents).ToHashSet();
        return relations.Where(row => ancestors.Contains(row.CategoryId)).Select(row => row.AttributeGroupId).ToHashSet();
    }

    /// <summary>统计最终分类图及关系图使选择失去适用性的不重复主型号数量；异常分类链同样计为不适用。</summary>
    public static int CountUnavailableMasters(IReadOnlyDictionary<Guid, Guid?> parents,
        IEnumerable<ProductCategoryAttributeGroupDto> relations, IEnumerable<ProductMasterAttributeSelectionDto> references)
    {
        var snapshot = relations.ToArray();
        var applicable = new Dictionary<Guid, IReadOnlySet<Guid>>();
        var invalidMasters = new HashSet<Guid>();
        foreach (var reference in references)
        {
            if (!applicable.TryGetValue(reference.CategoryId, out var groups))
            {
                try { groups = GetApplicableGroupIds(reference.CategoryId, parents, snapshot); }
                catch (InvalidOperationException) { groups = new HashSet<Guid>(); }
                applicable.Add(reference.CategoryId, groups);
            }
            if (!groups.Contains(reference.AttributeGroupId)) invalidMasters.Add(reference.ProductMasterId);
        }
        return invalidMasters.Count;
    }
}

using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products;

/// <summary>校验一次批量保存后的当前 BOM 图，不访问数据库。</summary>
public sealed class BomStructureValidator
{
    public string? Validate(
        IReadOnlyList<Bom> currentGraph,
        IReadOnlyCollection<Guid> affectedParentIds,
        IReadOnlyDictionary<Guid, Product> affectedProducts)
    {
        ArgumentNullException.ThrowIfNull(currentGraph);
        ArgumentNullException.ThrowIfNull(affectedParentIds);
        ArgumentNullException.ThrowIfNull(affectedProducts);

        var activeLines = currentGraph.Where(x => !x.IsDeleted).ToArray();
        foreach (var parentId in affectedParentIds.Distinct())
        {
            if (!affectedProducts.TryGetValue(parentId, out var parent))
                return $"BOM 父产品 {parentId} 不存在或已删除。";

            var parentError = CustomerSuppliedMaterialPolicy.ValidateBomParent(parent);
            if (parentError is not null) return parentError;

            var lines = activeLines
                .Where(x => x.ParentProductId == parentId && x.BomVersion == parent.BomVersion)
                .OrderBy(x => x.LineNumber)
                .ToArray();
            if (parent.BomVersion > 0 && lines.Length == 0)
                return $"产品 {parent.Sku} 的 BOM 版本为 {parent.BomVersion}，至少需要保留一条组成行。";

            var duplicateComponent = lines
                .Where(x => x.ComponentProductId.HasValue)
                .GroupBy(x => x.ComponentProductId!.Value)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateComponent is not null)
            {
                var sku = lines.First(x => x.ComponentProductId == duplicateComponent.Key).ComponentSkuSnapshot;
                return $"BOM {parent.BomNo} 中子件 {sku} 重复。";
            }

            foreach (var line in lines)
            {
                if (!line.ComponentProductId.HasValue
                    || !affectedProducts.TryGetValue(line.ComponentProductId.Value, out var child))
                    return $"BOM {parent.BomNo} 第 {line.LineNumber} 行的子产品不存在或已删除。";

                var lineError = CustomerSuppliedMaterialPolicy.ValidateBomLine(line, child);
                if (lineError is not null) return lineError;

                if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(child))
                {
                    var childHasBom = activeLines.Any(x => x.ParentProductId == child.Uid);
                    var childError = CustomerSuppliedMaterialPolicy.ValidateProduct(child, childHasBom);
                    if (childError is not null) return childError;
                }
            }
        }

        var childrenByParent = activeLines
            .Where(x => x.ParentProductId.HasValue && x.ComponentProductId.HasValue)
            .GroupBy(x => x.ParentProductId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x.Select(line => line.ComponentProductId!.Value).Distinct().ToArray());

        foreach (var parentId in affectedParentIds.Distinct())
        {
            var visiting = new HashSet<Guid>();
            var visited = new HashSet<Guid>();
            if (HasCycle(parentId, childrenByParent, visiting, visited))
            {
                var parentName = affectedProducts.TryGetValue(parentId, out var parent)
                    ? parent.Sku
                    : parentId.ToString();
                return $"BOM {parentName} 存在循环依赖。";
            }
        }

        return null;
    }

    private static bool HasCycle(
        Guid productId,
        IReadOnlyDictionary<Guid, Guid[]> childrenByParent,
        HashSet<Guid> visiting,
        HashSet<Guid> visited)
    {
        if (visited.Contains(productId)) return false;
        if (!visiting.Add(productId)) return true;

        if (childrenByParent.TryGetValue(productId, out var children))
        {
            foreach (var childId in children)
                if (HasCycle(childId, childrenByParent, visiting, visited)) return true;
        }

        visiting.Remove(productId);
        visited.Add(productId);
        return false;
    }
}

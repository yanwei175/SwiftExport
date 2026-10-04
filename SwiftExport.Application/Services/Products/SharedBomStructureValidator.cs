using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Services.Products;

/// <summary>按数据库确认的共享组构造产品有效边，只校验受影响结构及其可达环路，不修改物理组成行。</summary>
public sealed class SharedBomStructureValidator
{
    /// <summary>校验受影响共享组的组成、全部父件资格及可达循环；其他组仅提供边，不因无关坏数据冻结资料维护。</summary>
    public string? Validate(IReadOnlyList<SharedBomGroup> groups, IReadOnlyCollection<long> affectedGroupKeys,
        IReadOnlyDictionary<Guid, Product> products)
    {
        var byKey = groups.ToDictionary(group => group.GroupKey);
        var byParent = groups.SelectMany(group => group.Products
                .Where(product => !product.IsDeleted && product.BomVersion > 0)
                .Select(product => (product.Uid, Group: group)))
            .ToDictionary(item => item.Uid, item => item.Group);
        foreach (var key in affectedGroupKeys.Distinct())
        {
            var group = byKey[key];
            var error = ValidateGroup(group, products);
            if (error is not null) return error;
        }

        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        foreach (var parent in affectedGroupKeys.Distinct().SelectMany(key => byKey[key].Products))
        {
            var error = Visit(parent.Uid);
            if (error is not null) return error;
        }
        return null;

        // 共享产品各自拥有有效出边；即使来源父没有该边，也必须发现其他共享父形成的自环及间接环。
        string? Visit(Guid productId)
        {
            if (visited.Contains(productId)) return null;
            if (!visiting.Add(productId))
                return $"BOM {(products.TryGetValue(productId, out var product) ? product.Sku : productId.ToString())} 存在循环依赖。";
            if (byParent.TryGetValue(productId, out var group))
            {
                if (group.Lines.Any(line => !line.IsDeleted && line.BomVersion != 1))
                    return $"BOM {group.BomNo} 存在异常组成有效标记，不能将其作为当前结构使用。";
                foreach (var childId in group.Lines.Where(line => !line.IsDeleted && line.ComponentProductId.HasValue)
                    .Select(line => line.ComponentProductId!.Value).Distinct())
                {
                    var error = Visit(childId);
                    if (error is not null) return error;
                }
            }
            visiting.Remove(productId);
            visited.Add(productId);
            return null;
        }
    }

    /// <summary>检查共享组标准组成和所有有效父产品；来源父字段仅保留追溯，不决定实际父子关系。</summary>
    private static string? ValidateGroup(SharedBomGroup group, IReadOnlyDictionary<Guid, Product> products)
    {
        var lines = group.Lines.Where(line => !line.IsDeleted).ToArray();
        if (lines.Any(line => line.BomVersion != 1))
            return $"BOM {group.BomNo} 存在异常组成有效标记，请先核实旧数据，不能自动启用或改写。";
        var parents = group.Products.Where(product => !product.IsDeleted && product.BomVersion > 0).ToArray();
        // 普通读取尚保留标准标记匹配；这是临时可见性保护，不把不同正数解释为组成版本。
        if (lines.Length > 0 && !parents.Any(product => product.BomVersion == 1))
            return $"BOM {group.BomNo} 的引用父有效标记尚未标准化，不能使非空组成在普通读取中不可见。";
        if (parents.Length > 0 && lines.Length == 0)
            return $"BOM {group.BomNo} 至少需要保留一条组成行。";
        if (lines.GroupBy(line => line.LineNumber).Any(rows => rows.Count() > 1))
            return $"BOM {group.BomNo} 中行号重复。";
        if (lines.Where(line => line.ComponentProductId.HasValue)
            .GroupBy(line => line.ComponentProductId!.Value).Any(rows => rows.Count() > 1))
            return $"BOM {group.BomNo} 中子件重复。";
        foreach (var parent in parents)
        {
            var parentError = CustomerSuppliedMaterialPolicy.ValidateBomParent(parent);
            if (parentError is not null) return parentError;
            foreach (var line in lines)
            {
                if (!line.ComponentProductId.HasValue
                    || !products.TryGetValue(line.ComponentProductId.Value, out var child) || child.IsDeleted)
                    return $"BOM {group.BomNo} 第 {line.LineNumber} 行的子产品不存在或已删除。";
                // 只创建策略输入，不向物理行回写共享父身份或来源快照。
                var edge = new Bom
                {
                    Uid = line.Uid, ParentProductId = parent.Uid, ComponentProductId = line.ComponentProductId,
                    LineNumber = line.LineNumber, Numerator = line.Numerator, Denominator = line.Denominator
                };
                var lineError = CustomerSuppliedMaterialPolicy.ValidateBomLine(edge, child);
                if (lineError is not null) return lineError;
                var childHasBom = child.BomVersion > 0 || !string.IsNullOrWhiteSpace(child.BomNo);
                var childError = CustomerSuppliedMaterialPolicy.ValidateProduct(child, childHasBom);
                if (childError is not null) return childError;
            }
        }
        return null;
    }
}

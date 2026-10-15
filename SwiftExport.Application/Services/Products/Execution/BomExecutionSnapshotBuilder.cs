using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwiftExport.Application.Services.Products.Execution;

/// <summary>纯构建器：验证完整当前图，以原始整数分数展开路径并生成稳定指纹。</summary>
internal static class BomExecutionSnapshotBuilder
{
    // 显式资源边界：超限拒绝整次读取，不截断图或静默丢弃分支。
    internal const int MaxNodes = 10000;
    private const int MaxPathLength = 4000;
    private static readonly BigInteger MaxScaledRatio = BigInteger.Parse("999999999999999999");

    /// <summary>从持锁读取的事实建立快照；路径比例只在输出节点时舍入，后续展开仍使用原始分数。</summary>
    internal static BomExecutionSnapshotDto Build(Guid rootProductId, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyList<Bom> allLines, IReadOnlyList<SysDictData> units, CancellationToken cancellationToken = default)
    {
        var activeUnits = units.Where(x => !x.IsDeleted && x.IsEnabled && x.DictCode == "Unit"
            && !string.IsNullOrWhiteSpace(x.DataNameEn)).ToArray();
        if (activeUnits.GroupBy(x => x.DataNameEn!.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidOperationException("启用单位代码重复，不能建立 BOM 执行依据。");
        var unitCodes = activeUnits.ToDictionary(x => x.DataNameEn!.Trim(), x => x.DataNameEn!.Trim(), StringComparer.OrdinalIgnoreCase);
        var byParent = allLines.Where(x => !x.IsDeleted && x.ParentProductId.HasValue)
            .GroupBy(x => x.ParentProductId!.Value).ToDictionary(x => x.Key, x => x.ToArray());
        var nodes = new List<BomExecutionNodeDto>();
        var visiting = new HashSet<Guid>();
        var usedProducts = new Dictionary<Guid, Product>();
        var usedLines = new Dictionary<Guid, Bom>();
        Walk(rootProductId, "", BigInteger.One, BigInteger.One);
        var root = usedProducts[rootProductId];
        var fingerprintInput = new
        {
            RootProductId = rootProductId,
            Products = usedProducts.Values.OrderBy(x => x.Uid).Select(x => new
            {
                x.Uid, x.Version, x.Sku, x.BomNo, x.BomVersion, x.MaterialType, x.SupplyType,
                Unit = unitCodes[x.Unit!.Trim()], x.IsActive, x.IsDeleted
            }).ToArray(),
            Lines = usedLines.Values.OrderBy(x => x.Uid).Select(x => new
            {
                x.Uid, x.Version, x.ParentProductId, x.ComponentProductId, x.BomNo, x.BomVersion,
                x.ParentSkuSnapshot, x.ComponentSkuSnapshot, x.LineNumber, x.Numerator, x.Denominator
            }).ToArray(),
            Nodes = nodes.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray()
        };
        return new BomExecutionSnapshotDto
        {
            RootProductId = rootProductId, HasBom = root.BomVersion > 0,
            Nodes = nodes.OrderBy(x => x.Path, StringComparer.Ordinal).ToList(),
            Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fingerprintInput)))
        };

        void Walk(Guid productId, string path, BigInteger numerator, BigInteger denominator)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!products.TryGetValue(productId, out var parent) || parent.Uid != productId)
                throw new InvalidOperationException($"BOM 产品 {productId} 缺失或身份不一致。");
            ValidateProduct(parent, unitCodes);
            if (!visiting.Add(productId)) throw new InvalidOperationException($"BOM 产品 {parent.Sku} 存在循环依赖。");
            usedProducts[productId] = parent;
            var parentLines = byParent.GetValueOrDefault(productId) ?? [];
            var isCustomerSupplied = parent.MaterialType == MaterialTypesEnum.客供件;
            if (isCustomerSupplied && (parent.BomVersion != 0 || !string.IsNullOrWhiteSpace(parent.BomNo) || parentLines.Length > 0
                || parent.SupplyType != SupplyTypeEnums.采购))
                throw new InvalidOperationException($"客供件 {parent.Sku} 必须是采购型终端物料，不能拥有 BOM。");
            if (parent.BomVersion == 0)
            {
                if (parentLines.Length > 0) throw new InvalidOperationException($"产品 {parent.Sku} 声明无 BOM 但存在组成行。");
                visiting.Remove(productId);
                return;
            }
            if (parent.BomVersion < 0 || string.IsNullOrWhiteSpace(parent.BomNo))
                throw new InvalidOperationException($"产品 {parent.Sku} 的当前 BOM 身份无效。");
            var current = parentLines.Where(x => x.BomVersion == parent.BomVersion && x.BomNo == parent.BomNo)
                .OrderBy(x => x.Uid).ToArray();
            if (current.Length == 0) throw new InvalidOperationException($"产品 {parent.Sku} 的当前 BOM 缺少组成行。");
            if (current.GroupBy(x => x.ComponentProductId).Any(x => x.Count() > 1))
                throw new InvalidOperationException($"产品 {parent.Sku} 的 BOM 存在同父重复组件。");
            foreach (var line in current)
            {
                if (line.Uid == Guid.Empty || line.ComponentProductId is not Guid childId || childId == Guid.Empty
                    || line.Numerator <= 0 || line.Denominator <= 0)
                    throw new InvalidOperationException($"产品 {parent.Sku} 的 BOM 行身份或用量比例无效。");
                var childPath = path.Length == 0 ? line.Uid.ToString("N") : $"{path}/{line.Uid:N}";
                if (childPath.Length > MaxPathLength || nodes.Count >= MaxNodes)
                    throw new InvalidOperationException("BOM 路径长度或展开节点超过安全上限，不能生成部分执行依据。");
                if (!products.TryGetValue(childId, out var child))
                    throw new InvalidOperationException($"BOM 组件 {childId} 不存在，不能保存未知组件。");
                ValidateProduct(child, unitCodes);
                var nextNumerator = numerator * line.Numerator;
                var nextDenominator = denominator * line.Denominator;
                var divisor = BigInteger.GreatestCommonDivisor(nextNumerator, nextDenominator);
                nextNumerator /= divisor;
                nextDenominator /= divisor;
                nodes.Add(new BomExecutionNodeDto
                {
                    Path = childPath, RootProductId = rootProductId, ParentProductId = productId,
                    ComponentProductId = childId, BomLineId = line.Uid, ParentBomNo = parent.BomNo,
                    ParentBomVersion = parent.BomVersion, ParentSkuSnapshot = parent.Sku,
                    ComponentSkuSnapshot = child.Sku, UnitSnapshot = unitCodes[child.Unit!.Trim()],
                    MaterialType = child.MaterialType, SupplyType = child.SupplyType,
                    Numerator = line.Numerator, Denominator = line.Denominator,
                    CumulativeRatio = RoundRatio(nextNumerator, nextDenominator),
                    IsCustomerSupplied = child.MaterialType == MaterialTypesEnum.客供件
                });
                if (usedLines.TryGetValue(line.Uid, out var other) && !ReferenceEquals(other, line))
                    throw new InvalidOperationException("BOM 组成行身份重复，不能生成执行依据。");
                usedLines[line.Uid] = line;
                Walk(childId, childPath, nextNumerator, nextDenominator);
            }
            visiting.Remove(productId);
        }
    }

    /// <summary>验证新执行引用的身份、生命周期、物料角色与启用单位；包材可参与消耗，虚拟件允许作为结构节点。</summary>
    private static void ValidateProduct(Product product, IReadOnlyDictionary<string, string> units)
    {
        if (product.Uid == Guid.Empty || product.IsDeleted || !product.IsActive || string.IsNullOrWhiteSpace(product.Sku))
            throw new InvalidOperationException($"产品 {product.Sku} 不存在、已删除或已停用。");
        if (product.MaterialType is not (MaterialTypesEnum.成品 or MaterialTypesEnum.零件 or MaterialTypesEnum.虚拟件 or MaterialTypesEnum.客供件 or MaterialTypesEnum.包材))
            throw new InvalidOperationException($"产品 {product.Sku} 的物料角色无效。");
        if (product.SupplyType is not (SupplyTypeEnums.采购 or SupplyTypeEnums.生产 or SupplyTypeEnums.外协))
            throw new InvalidOperationException($"产品 {product.Sku} 的供货类型无效。");
        if (string.IsNullOrWhiteSpace(product.Unit) || !units.ContainsKey(product.Unit.Trim()))
            throw new InvalidOperationException($"产品 {product.Sku} 缺少有效启用基础单位。");
    }

    /// <summary>精确分数转换为 decimal(18,4)，中点远离零；正数下溢或溢出均拒绝。</summary>
    private static decimal RoundRatio(BigInteger numerator, BigInteger denominator)
    {
        var scaled = BigInteger.DivRem(numerator * 10000, denominator, out var remainder);
        if (remainder * 2 >= denominator) scaled++;
        if (scaled <= 0 || scaled > MaxScaledRatio)
            throw new InvalidOperationException("BOM 累计用量超出 decimal(18,4) 精度范围或正数舍入为零。");
        return (decimal)scaled / 10000m;
    }
}

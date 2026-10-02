using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment.Models;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;

/// <summary>无数据库副作用的毛需求拆解计算器。</summary>
public sealed class GrossDemandDecompositionPlanner
{
    /// <summary>依据来源快照、根产品和当前 BOM 生成建立计划，不写数据库。</summary>
    public GrossDemandDecompositionPlan BuildPlan(
        GrossDemand grossDemand,
        Product? rootProduct,
        IReadOnlyList<Bom> bomLines,
        IReadOnlyDictionary<Guid, Product> products,
        int nextRevision)
    {
        ArgumentNullException.ThrowIfNull(grossDemand);
        ArgumentNullException.ThrowIfNull(bomLines);
        ArgumentNullException.ThrowIfNull(products);

        if (grossDemand.DemandQty <= 0)
            return GrossDemandDecompositionPlan.Fail("INVALID_QUANTITY", "毛需求数量必须大于零。");
        if (nextRevision <= 0)
            return GrossDemandDecompositionPlan.Fail("INVALID_REVISION", "拆解版本必须大于零。");

        if (grossDemand.IsNonStandard)
            return BuildNonStandardPlan(grossDemand, nextRevision);
        if (!grossDemand.ProductId.HasValue || rootProduct is null || rootProduct.Uid != grossDemand.ProductId.Value)
            return GrossDemandDecompositionPlan.Fail("PRODUCT_NOT_FOUND", "毛需求关联的产品不存在或已删除。");
        if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(rootProduct))
            return GrossDemandDecompositionPlan.Fail("CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED", "客供件不能作为标准毛需求根产品。");
        if ((rootProduct.BomVersion > 0) != !string.IsNullOrWhiteSpace(rootProduct.BomNo))
            return GrossDemandDecompositionPlan.Fail("INVALID_PRODUCT_BOM", $"产品 {rootProduct.Sku} 的 BOM 编号和版本不一致。");
        if (rootProduct.BomVersion > 0 && bomLines.Count == 0)
            return GrossDemandDecompositionPlan.Fail("BOM_NOT_FOUND", $"产品 {rootProduct.Sku} 指定的 BOM 版本没有有效明细。");

        var orderedLines = bomLines.OrderBy(x => x.LineNumber).ThenBy(x => x.Uid).ToArray();
        foreach (var line in orderedLines)
        {
            if (!line.ComponentProductId.HasValue || !products.TryGetValue(line.ComponentProductId.Value, out var child))
                return GrossDemandDecompositionPlan.Fail("BOM_CHILD_NOT_FOUND", $"BOM 第 {line.LineNumber} 行未关联有效子产品。");
            var error = CustomerSuppliedMaterialPolicy.ValidateBomLine(line, child);
            if (error is not null)
                return GrossDemandDecompositionPlan.Fail("INVALID_BOM_LINE", error);
        }

        var customerSuppliedLines = orderedLines
            .Select(line => new CustomerSuppliedBomLine(line, products[line.ComponentProductId!.Value]))
            .Where(x => CustomerSuppliedMaterialPolicy.IsCustomerSupplied(x.Product))
            .ToArray();
        if (string.Equals(rootProduct.SupplyType, SupplyTypeEnums.生产, StringComparison.Ordinal)
            && customerSuppliedLines.Length > 0)
            return GrossDemandDecompositionPlan.Fail(
                "PRODUCTION_CUSTOMER_SUPPLIED_NOT_SUPPORTED",
                "生产型根产品包含客供件，当前版本尚未定义生产供料流程，不能拆解。");
        if (!string.Equals(rootProduct.SupplyType, SupplyTypeEnums.采购, StringComparison.Ordinal)
            && !string.Equals(rootProduct.SupplyType, SupplyTypeEnums.外协, StringComparison.Ordinal)
            && !string.Equals(rootProduct.SupplyType, SupplyTypeEnums.生产, StringComparison.Ordinal))
            return GrossDemandDecompositionPlan.Fail("UNSUPPORTED_SUPPLY_TYPE", $"产品 {rootProduct.Sku} 的供货类型无效。");

        return BuildRootPlan(grossDemand, rootProduct, customerSuppliedLines, nextRevision);
    }

    /// <summary>非标来源只形成一条采购型根物料需求，不引入库存供货推断。</summary>
    private static GrossDemandDecompositionPlan BuildNonStandardPlan(GrossDemand grossDemand, int revision)
    {
        var uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid();
        return GrossDemandDecompositionPlan.Ok([
            new NetDemand
            {
                Uid = uid,
                GrossDemandId = grossDemand.Uid,
                DecompositionRevision = revision,
                LineNumber = 1,
                LifecycleStatus = NetDemandLifecycleStatusEnum.Active,
                RootNetDemandId = uid,
                DeliveryMode = DemandDeliveryModeEnum.Standard,
                ProductId = null,
                MaterialTypeSnapshot = null,
                SupplyTypeSnapshot = SupplyTypeEnums.采购,
                SkuSnapshot = grossDemand.SkuSnapshot,
                CustomerSkuSnapshot = grossDemand.CustomerSkuSnapshot,
                DescriptionSnapshot = grossDemand.DescriptionSnapshot,
                ColorSnapshot = grossDemand.ColorSnapshot,
                SpecificationSnapshot = grossDemand.SpecificationSnapshot,
                UnitSnapshot = NormalizeUnit(grossDemand.UnitSnapshot),
                GrossQty = grossDemand.DemandQty,
                NetQty = grossDemand.DemandQty,
                RequiredDate = grossDemand.RequiredDate,
                Remark = grossDemand.Remark
            }
        ]);
    }

    /// <summary>从标准根产品及直接客供子件生成物料行，保持原父子数量关系。</summary>
    private static GrossDemandDecompositionPlan BuildRootPlan(
        GrossDemand grossDemand,
        Product rootProduct,
        IReadOnlyList<CustomerSuppliedBomLine> customerSuppliedLines,
        int revision)
    {
        var rootUid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid();
        var root = new NetDemand
        {
            Uid = rootUid,
            GrossDemandId = grossDemand.Uid,
            DecompositionRevision = revision,
            LineNumber = 1,
            LifecycleStatus = NetDemandLifecycleStatusEnum.Active,
            RootNetDemandId = rootUid,
            DeliveryMode = DemandDeliveryModeEnum.Standard,
            ProductId = rootProduct.Uid,
            BomVersion = rootProduct.BomVersion > 0 ? rootProduct.BomVersion : null,
            BomPath = rootProduct.Sku,
            MaterialTypeSnapshot = rootProduct.MaterialType,
            SupplyTypeSnapshot = rootProduct.SupplyType,
            SkuSnapshot = grossDemand.SkuSnapshot,
            ParentSkuSnapshot = string.Empty,
            CustomerSkuSnapshot = grossDemand.CustomerSkuSnapshot,
            DescriptionSnapshot = grossDemand.DescriptionSnapshot,
            ColorSnapshot = grossDemand.ColorSnapshot,
            SpecificationSnapshot = grossDemand.SpecificationSnapshot,
            UnitSnapshot = NormalizeUnit(grossDemand.UnitSnapshot),
            GrossQty = grossDemand.DemandQty,
            NetQty = grossDemand.DemandQty,
            RequiredDate = grossDemand.RequiredDate,
            Remark = grossDemand.Remark
        };
        var rows = new List<NetDemand> { root };
        var lineNumber = 2;
        foreach (var entry in customerSuppliedLines)
        {
            var line = entry.Line;
            var child = entry.Product;
            var childError = CustomerSuppliedMaterialPolicy.ValidateProduct(child, hasActiveBom: false);
            if (childError is not null)
                return GrossDemandDecompositionPlan.Fail("INVALID_CUSTOMER_SUPPLIED_PRODUCT", childError);
            rows.Add(new NetDemand
            {
                Uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid(),
                GrossDemandId = grossDemand.Uid,
                DecompositionRevision = revision,
                LineNumber = lineNumber++,
                LifecycleStatus = NetDemandLifecycleStatusEnum.Active,
                ParentNetDemandId = rootUid,
                RootNetDemandId = rootUid,
                DeliveryTargetNetDemandId = rootUid,
                DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier,
                ProductId = child.Uid,
                BomLineId = line.Uid,
                BomVersion = line.BomVersion,
                BomPath = $"{rootProduct.Sku}/{child.Sku}",
                MaterialTypeSnapshot = child.MaterialType,
                SupplyTypeSnapshot = child.SupplyType,
                SkuSnapshot = child.Sku,
                ParentSkuSnapshot = rootProduct.Sku,
                DescriptionSnapshot = child.DescriptionEn,
                SpecificationSnapshot = child.FeaturesEn,
                UnitSnapshot = NormalizeUnit(child.Unit),
                GrossQty = CalculateComponentQty(grossDemand.DemandQty, line.Numerator, line.Denominator),
                NetQty = CalculateComponentQty(grossDemand.DemandQty, line.Numerator, line.Denominator),
                RequiredDate = grossDemand.RequiredDate,
                Remark = line.Note
            });
        }
        return GrossDemandDecompositionPlan.Ok(rows);
    }

    /// <summary>按 BOM 分子和分母换算子件数量，拒绝非正数量与比例。</summary>
    public static decimal CalculateComponentQty(decimal parentQty, int numerator, int denominator)
    {
        if (parentQty <= 0) throw new ArgumentOutOfRangeException(nameof(parentQty));
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        return parentQty * numerator / denominator;
    }

    /// <summary>规范单位文本而不推断缺失单位，也不执行单位换算。</summary>
    private static string? NormalizeUnit(string? unit) => string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();

    private sealed record CustomerSuppliedBomLine(Bom Line, Product Product);
}


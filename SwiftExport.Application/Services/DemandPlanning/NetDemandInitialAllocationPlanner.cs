using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>无数据库副作用的初始净需求计划计算器。</summary>
public sealed class NetDemandInitialAllocationPlanner
{
    public NetDemandInitialAllocationPlan Build(
        IReadOnlyList<NetDemand> netDemands,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Supplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(netDemands);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(suppliers);
        if (netDemands.Count == 0)
            return NetDemandInitialAllocationPlan.Fail("EMPTY_NET_DEMAND", "拆解结果没有净需求，不能建立初始计划。");

        var allocations = new List<NetDemandAllocation>(netDemands.Count);
        var allocationByDemand = new Dictionary<Guid, NetDemandAllocation>();
        foreach (var demand in netDemands.OrderBy(x => x.LineNumber).ThenBy(x => x.Uid))
        {
            if (demand.NetQty <= 0)
                return NetDemandInitialAllocationPlan.Fail("INVALID_NET_QUANTITY", $"净需求 {demand.SkuSnapshot} 的数量必须大于零。");
            var executionType = ResolveExecutionType(demand.SupplyTypeSnapshot);
            if (!executionType.HasValue)
                return NetDemandInitialAllocationPlan.Fail("UNSUPPORTED_SUPPLY_TYPE", $"净需求 {demand.SkuSnapshot} 的供货类型无效。");

            Product? product = null;
            if (demand.ProductId.HasValue && !products.TryGetValue(demand.ProductId.Value, out product))
                return NetDemandInitialAllocationPlan.Fail("PRODUCT_NOT_FOUND", $"净需求 {demand.SkuSnapshot} 的产品不存在或已删除。");

            Supplier? supplier = null;
            if (executionType == DemandExecutionTypeEnum.Purchase && product?.DefaultSupplierId is Guid supplierId)
            {
                if (!suppliers.TryGetValue(supplierId, out supplier))
                    return NetDemandInitialAllocationPlan.Fail("INVALID_DEFAULT_SUPPLIER", $"产品 {product.Sku} 的默认供应商已失效。");
                if (!string.IsNullOrWhiteSpace(product.DefaultSupplierCode)
                    && !string.Equals(product.DefaultSupplierCode.Trim(), supplier.SupplierCode, StringComparison.OrdinalIgnoreCase))
                    return NetDemandInitialAllocationPlan.Fail("INVALID_DEFAULT_SUPPLIER", $"产品 {product.Sku} 的默认供应商 Uid 与编码不一致。");
            }

            var allocation = new NetDemandAllocation
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                NetDemandId = demand.Uid,
                PlanRevision = 1,
                Sequence = 1,
                LifecycleStatus = NetDemandAllocationLifecycleStatusEnum.Active,
                ExecutionType = executionType.Value,
                PlannedQty = demand.NetQty,
                DemandRequiredDate = demand.RequiredDate,
                PlannedDeliveryDate = demand.RequiredDate,
                PurchaseSupplierId = supplier?.Uid,
                SupplierCodeSnapshot = supplier?.SupplierCode,
                SupplierNameSnapshot = supplier?.SupplierName,
                AssignmentSource = supplier is null
                    ? SupplierAssignmentSourceEnum.Unassigned
                    : SupplierAssignmentSourceEnum.ProductDefault
            };
            allocations.Add(allocation);
            allocationByDemand.Add(demand.Uid, allocation);
        }

        foreach (var demand in netDemands.Where(x => x.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier))
        {
            if (!demand.DeliveryTargetNetDemandId.HasValue
                || !allocationByDemand.TryGetValue(demand.DeliveryTargetNetDemandId.Value, out var target))
                return NetDemandInitialAllocationPlan.Fail("INVALID_DELIVERY_TARGET", $"客供件 {demand.SkuSnapshot} 没有可证明的父件计划。");
            var allocation = allocationByDemand[demand.Uid];
            allocation.DeliveryTargetAllocationId = target.Uid;
            allocation.DeliveryTargetSupplierId = target.PurchaseSupplierId;
            allocation.DeliverySupplierCodeSnapshot = target.SupplierCodeSnapshot;
            allocation.DeliverySupplierNameSnapshot = target.SupplierNameSnapshot;
        }

        return NetDemandInitialAllocationPlan.Ok(allocations);
    }

    private static DemandExecutionTypeEnum? ResolveExecutionType(string supplyType)
    {
        if (string.Equals(supplyType, SupplyTypeEnums.采购, StringComparison.Ordinal)) return DemandExecutionTypeEnum.Purchase;
        if (string.Equals(supplyType, SupplyTypeEnums.生产, StringComparison.Ordinal)) return DemandExecutionTypeEnum.Production;
        if (string.Equals(supplyType, SupplyTypeEnums.外协, StringComparison.Ordinal)) return DemandExecutionTypeEnum.Outsourcing;
        return null;
    }
}

public sealed class NetDemandInitialAllocationPlan
{
    private NetDemandInitialAllocationPlan(
        bool isValid,
        string? errorCode,
        string? errorMessage,
        IReadOnlyList<NetDemandAllocation> allocations)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Allocations = allocations;
    }

    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<NetDemandAllocation> Allocations { get; }

    public static NetDemandInitialAllocationPlan Ok(IReadOnlyList<NetDemandAllocation> allocations)
        => new(true, null, null, allocations);

    public static NetDemandInitialAllocationPlan Fail(string code, string message)
        => new(false, code, message, []);
}

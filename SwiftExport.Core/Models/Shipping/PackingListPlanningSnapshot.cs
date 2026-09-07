using SwiftExport.Core.Entities.Shipping;

namespace SwiftExport.Core.Models.Shipping;

public sealed class PackingListPlanningSnapshot
{
    public PackingListDocument Document { get; set; } = new();
    public IReadOnlyList<PackingListPlanningLineSnapshot> Lines { get; set; } = [];
}

public sealed class PackingListPlanningLineSnapshot
{
    public PackingListContributionLine Line { get; set; } = new();
    public Guid CustomerId { get; set; }
    public bool IsSourceActive { get; set; }
    public bool IsProductActive { get; set; }
    public bool IsWarehouseActive { get; set; }
    /// <summary>来源需求当前并发版本。</summary>
    public int DemandVersion { get; set; }
    /// <summary>上游来源单据的当前事实版本。</summary>
    public int? SourceVersion { get; set; }
    /// <summary>来源需求是否具有可证明的订单池归属；缺失时正式库存动作必须阻断。</summary>
    public bool HasOrderPoolOwnership { get; set; }
    public decimal PlannedQty { get; set; }
    public decimal ShippedNetQty { get; set; }
    public decimal SourceInboundNetQty { get; set; }
    public decimal ReadyQty { get; set; }
    public decimal BalanceQty { get; set; }
    public int BalanceVersion { get; set; }
    public IReadOnlyList<PackingListCustomerSuppliedSnapshot> CustomerSuppliedItems { get; set; } = [];
}

public sealed class PackingListCustomerSuppliedSnapshot
{
    public Guid AllocationId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal PlannedQty { get; set; }
    public decimal ParentPlannedQty { get; set; }
    public decimal HistoricalConsumptionQty { get; set; }
    public decimal BalanceQty { get; set; }
    public int BalanceVersion { get; set; }
}

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
    public int SourceVersion { get; set; }
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

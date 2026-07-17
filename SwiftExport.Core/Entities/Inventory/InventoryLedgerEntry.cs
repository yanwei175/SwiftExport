using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

[Table("InventoryTransaction")]
public sealed class InventoryLedgerEntry : BaseEntity
{
    public long Sequence { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public Guid ProductId { get; set; }
    public InventoryTransactionTypeEnum TransactionType { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public Guid? ReversalOfLedgerId { get; set; }
    public Guid CommandId { get; set; }
    public Guid CommandItemId { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredDate { get; set; }
    public string? WarehouseCodeSnapshot { get; set; }
    public string? SkuSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
}

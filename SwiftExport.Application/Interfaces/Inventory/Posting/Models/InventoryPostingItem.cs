using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory.Posting;

/// <summary>描述一条普通库存增减事实；快照缺省时由锁定主数据补齐。</summary>
public sealed class InventoryPostingItem
{
    public Guid CommandItemId { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public Guid ProductId { get; set; }
    public string? SkuSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public string? ProductNameSnapshot { get; set; }
    public decimal QuantityDelta { get; set; }
    public InventoryTransactionTypeEnum TransactionType { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? SourceDocumentNoSnapshot { get; set; }
    public Guid? ReversalOfLedgerId { get; set; }
    public Guid? ReplacementOfLedgerId { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredDate { get; set; }
}


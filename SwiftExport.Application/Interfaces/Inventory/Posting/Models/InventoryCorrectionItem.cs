namespace SwiftExport.Application.Interfaces.Inventory.Posting;

/// <summary>描述原子纠错中的一对完整反向与替代事实。</summary>
public sealed class InventoryCorrectionItem
{
    public Guid OriginalLedgerId { get; set; }
    public Guid ReverseCommandItemId { get; set; }
    public string ReverseSourceDocumentType { get; set; } = string.Empty;
    public Guid? ReverseSourceDocumentId { get; set; }
    public Guid? ReverseSourceLineId { get; set; }
    public string? ReverseSourceDocumentNoSnapshot { get; set; }
    public DateTime ReverseOccurredDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public InventoryPostingItem Replacement { get; set; } = new();
}


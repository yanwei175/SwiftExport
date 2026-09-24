namespace SwiftExport.Application.Interfaces.Inventory.Posting;

/// <summary>描述一条完整反向请求；库存身份和数量不由调用方覆盖。</summary>
public sealed class InventoryReverseItem
{
    public Guid CommandItemId { get; set; }
    public Guid OriginalLedgerId { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? SourceDocumentNoSnapshot { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredDate { get; set; }
}


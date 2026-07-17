using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>库存写入内核，仅限 Application 内部业务单据服务调用。</summary>
public interface IInventoryPostingService
{
    Task<IReadOnlyList<InventoryPostingResultDto>> PostAsync(Guid commandId, IReadOnlyList<InventoryPostingItem> items, CancellationToken cancellationToken = default);
}

public sealed class InventoryPostingItem
{
    public Guid CommandItemId { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityDelta { get; set; }
    public InventoryTransactionTypeEnum TransactionType { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? Reason { get; set; }
    public bool AllowNegativeStock { get; set; }
    public DateTime OccurredDate { get; set; }
}

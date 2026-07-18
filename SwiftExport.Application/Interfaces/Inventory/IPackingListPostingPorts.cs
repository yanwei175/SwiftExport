using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Interfaces.Inventory;

public interface IInboundPostingPort
{
    Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostAutoReceiptsAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate,
        IReadOnlyList<PackingListPostingPortLine> lines, CancellationToken cancellationToken = default);
    Task ReverseAutoReceiptsAsync(Guid packingListId, Guid commandId, string reason, CancellationToken cancellationToken = default);
}

public interface IOutboundPostingPort
{
    Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate,
        IReadOnlyList<PackingListPostingPortLine> finalShipmentLines,
        IReadOnlyList<PackingListPostingPortLine> customerSuppliedLines,
        CancellationToken cancellationToken = default);
    Task ReverseAsync(Guid packingListId, Guid commandId, string reason, CancellationToken cancellationToken = default);
}

public sealed class PackingListPostingPortLine
{
    public Guid CommandItemId { get; set; }
    public Guid ContributionId { get; set; }
    public Guid NetDemandAllocationId { get; set; }
    public Guid? GrossDemandId { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public string? SalesOrderNo { get; set; }
    public decimal Quantity { get; set; }
}

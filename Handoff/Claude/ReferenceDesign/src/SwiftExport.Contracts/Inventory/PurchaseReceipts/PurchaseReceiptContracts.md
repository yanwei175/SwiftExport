# PurchaseReceipts — Contracts

`SwiftExport.Contracts/Inventory/PurchaseReceipts/`

```csharp
public enum PurchaseReceiptStatus { Draft = 0, Posted = 1, Reversed = 2 }

public class PurchaseReceiptDto
{
	public Guid Id { get; set; }
	public string DocumentNo { get; set; }
	public DateTime DocumentDate { get; set; }
	public PurchaseReceiptStatus Status { get; set; }
	public Guid SupplierId { get; set; }   public string SupplierName { get; set; }
	public Guid WarehouseId { get; set; }  public string WarehouseName { get; set; }
	public string Remark { get; set; }
	public int Version { get; set; }
	public string PostedBy { get; set; }   public DateTimeOffset? PostedAt { get; set; }
	public string ReversedBy { get; set; } public DateTimeOffset? ReversedAt { get; set; } public string ReverseReason { get; set; }
	public List<PurchaseReceiptLineDto> Lines { get; set; } = new List<PurchaseReceiptLineDto>();
}

public class PurchaseReceiptLineDto
{
	public Guid Id { get; set; }
	public int LineNo { get; set; }
	public Guid PurchaseOrderId { get; set; }  public string PurchaseOrderNo { get; set; }
	public Guid PurchaseOrderLineId { get; set; }                 // 来源行
	public Guid ProductId { get; set; }  public string ProductCode { get; set; }  public string ProductName { get; set; }  public string Unit { get; set; }
	public decimal Quantity { get; set; }
}

public class SavePurchaseReceiptRequest
{
	public Guid? Id { get; set; }
	public int Version { get; set; }
	public DateTime DocumentDate { get; set; }
	public Guid SupplierId { get; set; }
	public Guid WarehouseId { get; set; }
	public string Remark { get; set; }
	public List<SavePurchaseReceiptLineRequest> Lines { get; set; } = new List<SavePurchaseReceiptLineRequest>();
}
public class SavePurchaseReceiptLineRequest { public Guid? Id; public Guid PurchaseOrderLineId; public decimal Quantity; }

public class ReverseDocumentRequest : DocumentCommandRequest { public string Reason { get; set; } }

public interface IPurchaseReceiptsApi
{
	[Get("/api/purchase-receipts")]               Task<PagedResult<PurchaseReceiptListItemDto>> SearchAsync([Query] PurchaseReceiptSearchRequest q);
	[Get("/api/purchase-receipts/{id}")]          Task<PurchaseReceiptDto> GetAsync(Guid id);
	[Post("/api/purchase-receipts")]              Task<PurchaseReceiptDto> SaveAsync([Body] SavePurchaseReceiptRequest r);
	[Delete("/api/purchase-receipts/{id}")]       Task DeleteAsync(Guid id, [Query] int version);
	PostAsync(Guid id, [Body]
	ReverseAsync(Guid id, [Body]
}
```

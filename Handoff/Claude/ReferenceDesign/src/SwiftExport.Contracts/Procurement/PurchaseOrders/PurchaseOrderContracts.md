# PurchaseOrders — Contracts

`SwiftExport.Contracts/Procurement/PurchaseOrders/`

```csharp
public enum PurchaseOrderStatus { Draft = 0, Confirmed = 1, Closed = 2, Voided = 9 }

public class PurchaseOrderDto
{
	public Guid Id { get; set; }
	public string DocumentNo { get; set; }
	public DateTime DocumentDate { get; set; }
	public PurchaseOrderStatus Status { get; set; }
	public Guid SupplierId { get; set; }
	public string SupplierName { get; set; }      // 快照
	public DateTime? ExpectedDate { get; set; }
	public decimal TotalAmount { get; set; }
	public string Remark { get; set; }
	public int Version { get; set; }
	public string ConfirmedBy { get; set; }  public DateTimeOffset? ConfirmedAt { get; set; }
	public string VoidedBy { get; set; }     public DateTimeOffset? VoidedAt { get; set; }  public string VoidReason { get; set; }
	public string ClosedBy { get; set; }     public DateTimeOffset? ClosedAt { get; set; }
	public List<PurchaseOrderLineDto> Lines { get; set; } = new List<PurchaseOrderLineDto>();
}

public class PurchaseOrderLineDto
{
	public Guid Id { get; set; }
	public int LineNo { get; set; }
	public Guid ProductId { get; set; }
	public string ProductCode { get; set; }  public string ProductName { get; set; }  public string Unit { get; set; }
	public decimal Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal Amount { get; set; }
	public decimal ReceivedQty { get; set; }      // 由入库过账/冲销维护
}

public class PurchaseOrderListItemDto { /* Id, DocumentNo, DocumentDate, Status, SupplierName, TotalAmount, ReceivedPercent */ }

public class PurchaseOrderSearchRequest : PagedRequest
{
	public PurchaseOrderStatus? Status { get; set; }
	public Guid? SupplierId { get; set; }
	public DateTime? DateFrom { get; set; }
	public DateTime? DateTo { get; set; }
}

// 保存草稿：新建与修改共用，Id 为空 = 新建；行 Id 为空 = 新行，缺失的旧行 = 删除
public class SavePurchaseOrderRequest
{
	public Guid? Id { get; set; }
	public int Version { get; set; }
	public DateTime DocumentDate { get; set; }
	public Guid SupplierId { get; set; }
	public DateTime? ExpectedDate { get; set; }
	public string Remark { get; set; }
	public List<SavePurchaseOrderLineRequest> Lines { get; set; } = new List<SavePurchaseOrderLineRequest>();
}
public class SavePurchaseOrderLineRequest { public Guid? Id; public Guid ProductId; public decimal Quantity; public decimal UnitPrice; }

// 状态命令：都带 CommandId（客户端每次操作新生成，重试复用，即 command_receipt.command_id）与 Version
public class DocumentCommandRequest
{
	public Guid CommandId { get; set; }
	public int Version { get; set; }
}
public class VoidDocumentRequest : DocumentCommandRequest { public string Reason { get; set; } }

public interface IPurchaseOrdersApi
{
	[Get("/api/purchase-orders")]                Task<PagedResult<PurchaseOrderListItemDto>> SearchAsync([Query] PurchaseOrderSearchRequest q);
	[Get("/api/purchase-orders/{id}")]           Task<PurchaseOrderDto> GetAsync(Guid id);
	[Post("/api/purchase-orders")]               Task<PurchaseOrderDto> SaveAsync([Body] SavePurchaseOrderRequest r);
	[Delete("/api/purchase-orders/{id}")]        Task DeleteAsync(Guid id, [Query] int version);
	ConfirmAsync(Guid id, [Body]
	UnconfirmAsync(Guid id, [Body]
	VoidAsync(Guid id, [Body]
	CloseAsync(Guid id, [Body]
}
```

所有命令返回最新的完整 `PurchaseOrderDto`，客户端直接替换界面数据，减少“成功但刷新失败”的情况。

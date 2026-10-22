# PurchaseReceipt（实体 + 状态机）

`SwiftExport.Domain/Inventory/PurchaseReceipts/PurchaseReceipt.cs`

```csharp
public sealed class PurchaseReceipt : DocumentEntity<PurchaseReceiptStatus>
{
	public Guid SupplierId { get; private set; }   public string SupplierName { get; private set; }
	public Guid WarehouseId { get; private set; }  public string WarehouseName { get; private set; }
	public string PostedBy { get; private set; }   public DateTimeOffset? PostedAt { get; private set; }
	public string ReversedBy { get; private set; } public DateTimeOffset? ReversedAt { get; private set; }
	public string ReverseReason { get; private set; }

	private readonly List<PurchaseReceiptLine> _lines = new();
	public IReadOnlyList<PurchaseReceiptLine> Lines => _lines;

	public static PurchaseReceipt CreateDraft(string no, DateTime date, Supplier s, Warehouse w) => new()
	{
		Id = Guid.CreateVersion7(), DocumentNo = no, DocumentDate = date, Status = PurchaseReceiptStatus.Draft,
		SupplierId = s.Id, SupplierName = s.Name, WarehouseId = w.Id, WarehouseName = w.Name
	};

	public void UpdateHeader(DateTime date, Supplier s, Warehouse w, string remark) { RequireStatus(PurchaseReceiptStatus.Draft); /* 赋值 */ }

	// 来源行由 Service 加载后传入；草稿阶段只做基本检查，真正的余量检查在过账时（余量可能在保存后变化）
	public void ReplaceLines(IEnumerable<(Guid? Id, PurchaseOrder Order, PurchaseOrderLine Source, decimal Qty)> input)
	{
		RequireStatus(PurchaseReceiptStatus.Draft);
		// 同 PurchaseOrder.ReplaceLines：增、改、删；
		// 检查 Order.SupplierId == SupplierId，Order.Status == Confirmed
	}

	public void MarkDeleted() { RequireStatus(PurchaseReceiptStatus.Draft); IsDeleted = true; }

	public void Post(string user, DateTimeOffset now)
	{
		RequireStatus(PurchaseReceiptStatus.Draft);
		if (_lines.Count == 0) throw new BusinessRuleException(ErrorCodes.InvalidStatus, "没有明细，不能过账。");
		Status = PurchaseReceiptStatus.Posted; PostedBy = user; PostedAt = now;
	}

	public void Reverse(string reason, string user, DateTimeOffset now)
	{
		RequireStatus(PurchaseReceiptStatus.Posted);
		if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException(ErrorCodes.Validation, "请填写冲销原因。");
		Status = PurchaseReceiptStatus.Reversed; ReverseReason = reason; ReversedBy = user; ReversedAt = now;
	}

	// 生成库存变动：过账为正，冲销为负
	public IEnumerable<StockMovement> ToMovements(int sign) =>
		_lines.Select(l => new StockMovement(WarehouseId, l.ProductId, sign * l.Quantity,
			SourceType: "PurchaseReceipt", SourceId: Id, SourceLineId: l.Id, Unit: l.Unit));
}

public sealed class PurchaseReceiptLine : Entity
{
	public Guid HeaderId { get; private set; }
	public int LineNo { get; private set; }
	public Guid PurchaseOrderId { get; private set; }  public string PurchaseOrderNo { get; private set; }
	public Guid PurchaseOrderLineId { get; private set; }
	public Guid ProductId { get; private set; }  public string ProductCode { get; private set; }
	public string ProductName { get; private set; }  public string Unit { get; private set; }
	public decimal Quantity { get; private set; }
}
```

`StockMovement` 是 `Domain/Inventory/Stock/StockMovement.cs` 中的值对象（record 可用，Domain 是 .NET 10）。

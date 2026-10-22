# PurchaseOrder / PurchaseOrderLine（实体 + 状态机）

`SwiftExport.Domain/Procurement/PurchaseOrders/PurchaseOrder.cs`

```csharp
public sealed class PurchaseOrder : DocumentEntity<PurchaseOrderStatus>
{
	public Guid SupplierId { get; private set; }
	public string SupplierName { get; private set; }
	public DateTime? ExpectedDate { get; private set; }
	public decimal TotalAmount { get; private set; }
	public string ConfirmedBy { get; private set; }  public DateTimeOffset? ConfirmedAt { get; private set; }
	public string VoidedBy { get; private set; }     public DateTimeOffset? VoidedAt { get; private set; }  public string VoidReason { get; private set; }
	public string ClosedBy { get; private set; }     public DateTimeOffset? ClosedAt { get; private set; }

	private readonly List<PurchaseOrderLine> _lines = new();
	public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

	// ---------- 创建与草稿编辑 ----------
	public static PurchaseOrder CreateDraft(string documentNo, DateTime date, Supplier supplier) => new()
	{
		Id = Guid.CreateVersion7(), DocumentNo = documentNo, DocumentDate = date,
		Status = PurchaseOrderStatus.Draft, SupplierId = supplier.Id, SupplierName = supplier.Name
	};

	public void UpdateHeader(DateTime date, Supplier supplier, DateTime? expected, string remark)
	{
		RequireStatus(PurchaseOrderStatus.Draft);
		DocumentDate = date; SupplierId = supplier.Id; SupplierName = supplier.Name;
		ExpectedDate = expected; Remark = remark;
	}

	// 以提交的行集合为准：有 Id 的更新，无 Id 的新增，缺失的删除
	public void ReplaceLines(IEnumerable<(Guid? Id, Product Product, decimal Qty, decimal Price)> input)
	{
		RequireStatus(PurchaseOrderStatus.Draft);
		var incoming = input.ToList();
		_lines.RemoveAll(l => incoming.All(i => i.Id != l.Id));
		var no = 1;
		foreach (var i in incoming)
		{
			var line = i.Id is { } id ? _lines.Single(l => l.Id == id) : AddLine();
			line.Set(no++, i.Product, i.Qty, i.Price);
		}
		TotalAmount = _lines.Sum(l => l.Amount);

		PurchaseOrderLine AddLine() { var l = new PurchaseOrderLine(Id); _lines.Add(l); return l; }
	}

	// ---------- 状态流转 ----------
	public void Confirm(string user, DateTimeOffset now)
	{
		RequireStatus(PurchaseOrderStatus.Draft);
		if (_lines.Count == 0) throw new BusinessRuleException(ErrorCodes.InvalidStatus, "没有明细，不能确认。");
		Status = PurchaseOrderStatus.Confirmed; ConfirmedBy = user; ConfirmedAt = now;
	}

	// hasDownstream 由 Application 查询后传入（实体不查库）
	public void Unconfirm(bool hasDownstream)
	{
		RequireStatus(PurchaseOrderStatus.Confirmed);
		if (hasDownstream || _lines.Any(l => l.ReceivedQty > 0))
			throw new BusinessRuleException(ErrorCodes.HasDownstream, "已有入库记录，不能取消确认。");
		Status = PurchaseOrderStatus.Draft; ConfirmedBy = null; ConfirmedAt = null;
	}

	public void Void(string reason, bool hasDownstream, string user, DateTimeOffset now)
	{
		RequireStatus(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Confirmed);
		if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException(ErrorCodes.Validation, "请填写作废原因。");
		if (hasDownstream) throw new BusinessRuleException(ErrorCodes.HasDownstream, "已有入库记录，不能作废。");
		Status = PurchaseOrderStatus.Voided; VoidReason = reason; VoidedBy = user; VoidedAt = now;
	}

	public void Close(string user, DateTimeOffset now)
	{
		RequireStatus(PurchaseOrderStatus.Confirmed);
		Status = PurchaseOrderStatus.Closed; ClosedBy = user; ClosedAt = now;
	}

	public void MarkDeleted()
	{
		RequireStatus(PurchaseOrderStatus.Draft);
		IsDeleted = true;
	}

	// ---------- 下游回写（由入库过账/冲销调用） ----------
	public void ApplyReceipt(Guid lineId, decimal qtyChange)
	{
		RequireStatus(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.Closed);
		var line = _lines.Single(l => l.Id == lineId);
		line.AddReceived(qtyChange);
		// 全部收齐自动关闭；冲销导致未收齐时自动重新打开（仅系统关闭的情况，可按业务决定）
	}
}

public sealed class PurchaseOrderLine : Entity
{
	public Guid HeaderId { get; private set; }
	public int LineNo { get; private set; }
	public Guid ProductId { get; private set; }
	public string ProductCode { get; private set; }  public string ProductName { get; private set; }  public string Unit { get; private set; }
	public decimal Quantity { get; private set; }
	public decimal UnitPrice { get; private set; }
	public decimal Amount { get; private set; }
	public decimal ReceivedQty { get; private set; }
	public decimal RemainingQty => Quantity - ReceivedQty;

	internal PurchaseOrderLine(Guid headerId) { Id = Guid.CreateVersion7(); HeaderId = headerId; }

	internal void Set(int no, Product p, decimal qty, decimal price)
	{
		if (!p.IsActive) throw new BusinessRuleException(ErrorCodes.ProductInactive, $"产品 {p.Code} 已停用。");
		LineNo = no; ProductId = p.Id; ProductCode = p.Code; ProductName = p.Name; Unit = p.Unit;
		Quantity = qty; UnitPrice = price; Amount = Math.Round(qty * price, 2);
	}

	internal void AddReceived(decimal change)
	{
		var next = ReceivedQty + change;
		if (next < 0 || next > Quantity)
			throw new BusinessRuleException(ErrorCodes.ExceedsRemaining, $"第 {LineNo} 行收货数量超出范围。");
		ReceivedQty = next;
	}
}
```

单元测试（`SwiftExport.Domain.Tests`）应覆盖每个方法的允许状态与拒绝状态，不需要数据库。

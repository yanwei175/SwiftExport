# PurchaseOrderRepository

`SwiftExport.Application/Procurement/PurchaseOrders/IPurchaseOrderRepository.cs`
`SwiftExport.Infrastructure/Procurement/PurchaseOrders/PurchaseOrderRepository.cs`

```csharp
public interface IPurchaseOrderRepository
{
	Task<PurchaseOrder?> GetForUpdateAsync(Guid id, CancellationToken ct);           // 头 + 行，头 FOR UPDATE
	Task<IReadOnlyList<PurchaseOrder>> GetManyForUpdateAsync(IEnumerable<Guid> ids, CancellationToken ct); // 按 id 升序加锁
	Task InsertAsync(PurchaseOrder po, CancellationToken ct);
	Task UpdateAsync(PurchaseOrder po, CancellationToken ct);                           // 头带版本条件 + 行差异保存
}

public sealed class PurchaseOrderRepository(IDbSession db, ICurrentUser user) : IPurchaseOrderRepository
{
	public async Task<PurchaseOrder?> GetForUpdateAsync(Guid id, CancellationToken ct)
	{
		var head = await db.Connection.QuerySingleOrDefaultAsync<PurchaseOrder>(new CommandDefinition(
			"SELECT * FROM purchase_order WHERE id=@id AND is_deleted=false FOR UPDATE", new { id }, db.Transaction, cancellationToken: ct));
		if (head is null) return null;
		var lines = await db.Connection.QueryAsync<PurchaseOrderLine>(new CommandDefinition(
			"SELECT * FROM purchase_order_line WHERE header_id=@id ORDER BY line_no", new { id }, db.Transaction, cancellationToken: ct));
		head.LoadLines(lines);   // internal 方法，仅供仓储还原
		return head;
	}

	public async Task UpdateAsync(PurchaseOrder po, CancellationToken ct)
	{
		var n = await db.Connection.ExecuteAsync(new CommandDefinition("""
			UPDATE purchase_order SET
			  document_date=@DocumentDate, status=@Status, supplier_id=@SupplierId, supplier_name=@SupplierName,
			  expected_date=@ExpectedDate, total_amount=@TotalAmount, remark=@Remark,
			  confirmed_by=@ConfirmedBy, confirmed_at=@ConfirmedAt, voided_by=@VoidedBy, voided_at=@VoidedAt,
			  void_reason=@VoidReason, closed_by=@ClosedBy, closed_at=@ClosedAt, is_deleted=@IsDeleted,
			  version=version+1, updated_at=now(), updated_by=@User
			WHERE id=@Id AND version=@Version
			""", new { /* po 字段 */ User = user.Name }, db.Transaction, cancellationToken: ct));
		if (n != 1) throw new ConcurrencyException();
		po.Version++;

		// 行差异保存：DELETE 不在集合中的行；UPSERT 其余行
		await db.Connection.ExecuteAsync(new CommandDefinition(
			"DELETE FROM purchase_order_line WHERE header_id=@Id AND NOT (id = ANY(@Ids))",
			new { po.Id, Ids = po.Lines.Select(l => l.Id).ToArray() }, db.Transaction, cancellationToken: ct));
		await db.Connection.ExecuteAsync(new CommandDefinition("""
			INSERT INTO purchase_order_line(id, header_id, line_no, product_id, product_code, product_name, unit,
											quantity, unit_price, amount, received_qty, version, created_at, created_by)
			VALUES (@Id, @HeaderId, @LineNo, @ProductId, @ProductCode, @ProductName, @Unit,
					@Quantity, @UnitPrice, @Amount, @ReceivedQty, 0, now(), @User)
			ON CONFLICT (id) DO UPDATE SET line_no=EXCLUDED.line_no, product_id=EXCLUDED.product_id,
				product_code=EXCLUDED.product_code, product_name=EXCLUDED.product_name, unit=EXCLUDED.unit,
				quantity=EXCLUDED.quantity, unit_price=EXCLUDED.unit_price, amount=EXCLUDED.amount,
				received_qty=EXCLUDED.received_qty, version=purchase_order_line.version+1,
				updated_at=now(), updated_by=@User
			""", po.Lines.Select(l => new { /* l 字段 */ User = user.Name }), db.Transaction, cancellationToken: ct));
	}
}
```

行不单独做版本检查：行只能通过已锁定且版本校验过的单据头修改。

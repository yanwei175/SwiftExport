# PurchaseOrderQueries

`SwiftExport.Application/Procurement/PurchaseOrders/IPurchaseOrderQueries.cs`
`SwiftExport.Infrastructure/Procurement/PurchaseOrders/PurchaseOrderQueries.cs`

```csharp
public interface IPurchaseOrderQueries
{
	Task<PagedResult<PurchaseOrderListItemDto>> SearchAsync(PurchaseOrderSearchRequest q, CancellationToken ct);
	Task<PurchaseOrderDto?> GetAsync(Guid id, CancellationToken ct);
	// 供入库单选择来源：已确认且有余量的行
	Task<IReadOnlyList<ReceivableLineDto>> GetReceivableLinesAsync(Guid supplierId, CancellationToken ct);
}

public sealed class PurchaseOrderQueries(IDbSession db) : IPurchaseOrderQueries
{
	public async Task<PurchaseOrderDto?> GetAsync(Guid id, CancellationToken ct)
	{
		using var multi = await db.Connection.QueryMultipleAsync(new CommandDefinition("""
			SELECT * FROM purchase_order WHERE id=@id AND is_deleted=false;
			SELECT * FROM purchase_order_line WHERE header_id=@id ORDER BY line_no;
			""", new { id }, cancellationToken: ct));
		var head = await multi.ReadSingleOrDefaultAsync<PurchaseOrderDto>();
		if (head is null) return null;
		head.Lines = (await multi.ReadAsync<PurchaseOrderLineDto>()).ToList();
		return head;
	}

	// SearchAsync：同 ProductQueries 形状；条件 status/supplier_id/document_date 区间；
	// ReceivedPercent 用 SUM(received_qty)/SUM(quantity) 子查询计算
}
```

查询不加锁、不开事务、不检查权限（Controller 已声明）。

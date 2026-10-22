# StockRepository

`SwiftExport.Infrastructure/Inventory/Stock/StockRepository.cs`

```csharp
public sealed class StockRepository(IDbSession db) : IStockRepository
{
	public async Task<StockBalance> GetOrCreateBalanceForUpdateAsync(Guid warehouseId, Guid productId, CancellationToken ct)
	{
		await db.Connection.ExecuteAsync(new CommandDefinition("""
			INSERT INTO stock_balance(warehouse_id, product_id, quantity, version)
			VALUES (@warehouseId, @productId, 0, 0)
			ON CONFLICT (warehouse_id, product_id) DO NOTHING
			""", new { warehouseId, productId }, db.Transaction, cancellationToken: ct));
		return await db.Connection.QuerySingleAsync<StockBalance>(new CommandDefinition("""
			SELECT * FROM stock_balance WHERE warehouse_id=@warehouseId AND product_id=@productId FOR UPDATE
			""", new { warehouseId, productId }, db.Transaction, cancellationToken: ct));
	}

	public async Task UpdateBalanceAsync(StockBalance b, CancellationToken ct)
	{
		var n = await db.Connection.ExecuteAsync(new CommandDefinition("""
			UPDATE stock_balance SET quantity=@Quantity, version=version+1
			WHERE warehouse_id=@WarehouseId AND product_id=@ProductId AND version=@Version
			""", b, db.Transaction, cancellationToken: ct));
		if (n != 1) throw new ConcurrencyException();
		b.Version++;
	}

	public Task InsertLedgerAsync(StockLedgerEntry e, CancellationToken ct) =>
		db.Connection.ExecuteAsync(new CommandDefinition("""
			INSERT INTO stock_ledger(id, warehouse_id, product_id, qty_change, balance_after,
									 source_type, source_id, source_line_id, reversal_of_id, at, by)
			VALUES (@Id, @WarehouseId, @ProductId, @QtyChange, @BalanceAfter,
					@SourceType, @SourceId, @SourceLineId, @ReversalOfId, @At, @By)
			""", e, db.Transaction, cancellationToken: ct));
}
```

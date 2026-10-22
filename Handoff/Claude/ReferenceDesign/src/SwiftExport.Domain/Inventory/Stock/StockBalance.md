# StockMovement / StockBalance

`SwiftExport.Domain/Inventory/Stock/`

```csharp
public sealed record StockMovement(Guid WarehouseId, Guid ProductId, decimal QtyChange,
	string SourceType, Guid SourceId, Guid SourceLineId, string Unit);

public sealed class StockBalance
{
	public Guid WarehouseId { get; private set; }
	public Guid ProductId { get; private set; }
	public decimal Quantity { get; private set; }
	public int Version { get; set; }

	public void Apply(decimal change, bool allowNegative = false)
	{
		var next = Quantity + change;
		if (next < 0 && !allowNegative)
			throw new BusinessRuleException(ErrorCodes.NegativeStock, "库存不足，操作后库存为负。");
		Quantity = next;
	}
}
```

流水（ledger）不是实体方法的结果，而是 `StockLedgerEntry` 记录，只追加不修改：
```csharp
public sealed record StockLedgerEntry(Guid Id, Guid WarehouseId, Guid ProductId, decimal QtyChange, decimal BalanceAfter,
	string SourceType, Guid SourceId, Guid SourceLineId, Guid? ReversalOfId, DateTimeOffset At, string By);
```

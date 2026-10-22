# StockService（库存记账；被各库存单据复用）

`SwiftExport.Application/Inventory/Stock/StockService.cs`

```csharp
// 必须在调用方的 uow 事务内调用；自己不开启/提交/回滚事务
public sealed class StockService(IStockRepository stock, ICurrentUser user, TimeProvider time)
{
	public async Task ApplyAsync(IEnumerable<StockMovement> movements, Guid? reversalOfSource, CancellationToken ct)
	{
		// 1. 按 (仓库, 产品) 合并并排序 → 固定加锁顺序，避免两个单据交叉加锁导致死锁
		var groups = movements.GroupBy(m => (m.WarehouseId, m.ProductId))
							  .OrderBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ProductId);

		foreach (var g in groups)
		{
			// 2. 余额行加锁（不存在则先插入 0）
			var balance = await stock.GetOrCreateBalanceForUpdateAsync(g.Key.WarehouseId, g.Key.ProductId, ct);
			foreach (var m in g)
			{
				balance.Apply(m.QtyChange);                         // 负库存 → 422
				await stock.InsertLedgerAsync(new StockLedgerEntry(Guid.CreateVersion7(),
					m.WarehouseId, m.ProductId, m.QtyChange, balance.Quantity,
					m.SourceType, m.SourceId, m.SourceLineId, reversalOfSource, time.GetLocalNow(), user.Name), ct);
			}
			await stock.UpdateBalanceAsync(balance, ct);             // WHERE version=@Version
		}
	}
}
```

为什么不需要“库存命令锁”或“同一命令重复记账检查”：
同一 CommandId 只执行一次
- 外层单据 `FOR UPDATE` + 状态检查（只有 Draft 能过账）已保证同一单据不会过账两次。
- 可再加数据库约束兜底：`stock_ledger` 上 `UNIQUE(source_type, source_line_id, reversal_of_id)`（`NULLS NOT DISTINCT`，PG15+）。

# PurchaseReceiptService（保存、删除、过账、冲销）

`SwiftExport.Application/Inventory/PurchaseReceipts/PurchaseReceiptService.cs`

```csharp
public sealed class PurchaseReceiptService(
	IUnitOfWork uow, ICommandExecutor commands,
	IPurchaseReceiptRepository receipts, IPurchaseOrderRepository orders,
	ISupplierRepository suppliers, IWarehouseRepository warehouses, IDocumentNumberGenerator numbers,
	StockService stock, IAuditLog audit, ICurrentUser user, TimeProvider time, ILogger<PurchaseReceiptService> log)
{
	// ===== 草稿保存 =====
	public Task<PurchaseReceiptDto> SaveAsync(SavePurchaseReceiptRequest r, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			// 与 PurchaseOrderService.SaveAsync 同形：加载/创建 → UpdateHeader → 加载来源行 → ReplaceLines → Insert/Update → 审计
			// 来源采购单只读加载（不加锁）：草稿阶段不占用余量
		}, ct);

	// ===== 删除草稿 ===== 同 PurchaseOrderService.DeleteAsync

	// ===== 过账 =====
	PostAsync(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		commands.ExecuteIdempotentAsync(r.CommandId,
		{
			// 1.
			var receipt = await receipts.GetForUpdateAsync(id, token) ?? throw new NotFoundException("入库单", id);
			receipt.EnsureVersion(r.Version);

			var poIds = receipt.Lines.Select(l => l.PurchaseOrderId).Distinct().Order();
			var pos = (await orders.GetManyForUpdateAsync(poIds, token)).ToDictionary(p => p.Id);

			// 2. 状态流转（实体检查 Draft）
			receipt.Post(user.Name, time.GetLocalNow());

			// 3. 回写来源：数量超出余量 / 采购单非已确认 → 422
			foreach (var line in receipt.Lines)
				pos[line.PurchaseOrderId].ApplyReceipt(line.PurchaseOrderLineId, line.Quantity);

			// 4. 库存：余额 + 流水
			await stock.ApplyAsync(receipt.ToMovements(+1), reversalOfSource: null, token);

			// 5. 保存
			await receipts.UpdateAsync(receipt, token);
			foreach (var po in pos.Values) await orders.UpdateAsync(po, token);

			// 6. 审计 + 业务日志
			await audit.WriteAsync(Operations.PurchaseReceipt.Post, id, new { Lines = receipt.Lines.Count }, token);
			log.LogInformation("入库单 {DocumentNo} 已过账，{LineCount} 行", receipt.DocumentNo, receipt.Lines.Count);

			return receipt.Adapt<PurchaseReceiptDto>();
		}, ct);   // 任一步异常 → 整个事务回滚：库存、流水、单据、采购单、审计、幂等记录全部不落库

	// ===== 冲销（取消过账）=====
	ReverseAsync(Guid id, ReverseDocumentRequest r, CancellationToken ct) =>
		commands.ExecuteIdempotentAsync(r.CommandId,
		{
			var receipt = await receipts.GetForUpdateAsync(id, token) ?? throw new NotFoundException("入库单", id);
			receipt.EnsureVersion(r.Version);
			var pos = (await orders.GetManyForUpdateAsync(
				receipt.Lines.Select(l => l.PurchaseOrderId).Distinct().Order(), token)).ToDictionary(p => p.Id);

			receipt.Reverse(r.Reason, user.Name, time.GetLocalNow());
			foreach (var line in receipt.Lines)
				pos[line.PurchaseOrderId].ApplyReceipt(line.PurchaseOrderLineId, -line.Quantity);

			// 反向流水，不删除原流水；库存不足（已被出库消耗）→ 422 负库存
			await stock.ApplyAsync(receipt.ToMovements(-1), reversalOfSource: receipt.Id, token);

			await receipts.UpdateAsync(receipt, token);
			foreach (var po in pos.Values) await orders.UpdateAsync(po, token);
			await audit.WriteAsync(Operations.PurchaseReceipt.Reverse, id, new { r.Reason }, token);
			return receipt.Adapt<PurchaseReceiptDto>();
		}, ct);
}
```

## 预览
需要“过账预览”（展示过账后各产品库存、各采购行余量）时，做成 `IPurchaseReceiptQueries.GetPostingPreviewAsync`：只读 SQL、不加锁、不开事务。
提交时不比对预览结果；只比对 Version。若业务要求“预览后库存变化必须重新确认”，再把相关余额版本加入请求（先确认确有此需求）。

## 这个方法读起来应能回答
- 加了哪些锁、什么顺序？→ 第 1 步。
- 哪些规则？→ 实体方法 `Post`、`ApplyReceipt`、`StockBalance.Apply`。
- 写了哪些表？→ 第 4、5、6 步。
- 失败怎么办？→ 抛异常，事务回滚，由全局处理器转换。

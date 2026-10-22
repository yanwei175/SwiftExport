# PurchaseOrderService（写用例：保存、删除、确认、取消确认、作废、关闭）

`SwiftExport.Application/Procurement/PurchaseOrders/PurchaseOrderService.cs`

```csharp
public sealed class PurchaseOrderService(
	IUnitOfWork uow, ICommandExecutor commands,
	IPurchaseOrderRepository orders, ISupplierRepository suppliers, IProductRepository products,
	IPurchaseReceiptRepository receipts, IDocumentNumberGenerator numbers,
	IAuditLog audit, ICurrentUser user, TimeProvider time)
{
	// ===== 草稿保存（新建 + 修改）：普通写入，不需要幂等 =====
	public Task<PurchaseOrderDto> SaveAsync(SavePurchaseOrderRequest r, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			var supplier = await suppliers.GetActiveAsync(r.SupplierId, token)
				?? throw new BusinessRuleException(ErrorCodes.InvalidStatus, "供应商不存在或已停用。");

			PurchaseOrder po;
			if (r.Id is null)
			{
				po = PurchaseOrder.CreateDraft(await numbers.NextAsync("PO", r.DocumentDate, token), r.DocumentDate, supplier);
			}
			else
			{
				po = await orders.GetForUpdateAsync(r.Id.Value, token) ?? throw new NotFoundException("采购单", r.Id);
				po.EnsureVersion(r.Version);
			}
			po.UpdateHeader(r.DocumentDate, supplier, r.ExpectedDate, r.Remark);

			var productMap = await products.GetManyAsync(r.Lines.Select(l => l.ProductId).Distinct(), token);
			po.ReplaceLines(r.Lines.Select(l => (l.Id,
				productMap.GetValueOrDefault(l.ProductId) ?? throw new NotFoundException("产品", l.ProductId),
				l.Quantity, l.UnitPrice)));

			if (r.Id is null) await orders.InsertAsync(po, token); else await orders.UpdateAsync(po, token);
			await audit.WriteAsync(r.Id is null ? Operations.PurchaseOrder.Create : Operations.PurchaseOrder.Update, po.Id, null, token);
			return po.Adapt<PurchaseOrderDto>();
		}, ct);

	// ===== 删除草稿 =====
	public Task DeleteAsync(Guid id, int version, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			var po = await orders.GetForUpdateAsync(id, token) ?? throw new NotFoundException("采购单", id);
			po.EnsureVersion(version);
			po.MarkDeleted();
			await orders.UpdateAsync(po, token);
			await audit.WriteAsync(Operations.PurchaseOrder.Delete, id, null, token);
		}, ct);

	// ===== 状态命令：幂等 =====
	ConfirmAsync(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		ExecuteIdempotentAsync(r.CommandId, Operations.PurchaseOrder.Confirm,
		{
			var po = await LoadAsync(id, r.Version, token);
			await EnsureReferencesActiveAsync(po, token);           // 供应商、产品仍有效（跨聚合，Service 负责）
			po.Confirm(user.Name, time.GetLocalNow());
			return await SaveAndAuditAsync(po, Operations.PurchaseOrder.Confirm, null, token);
		}, ct);

	UnconfirmAsync(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		ExecuteIdempotentAsync(r.CommandId, Operations.PurchaseOrder.Unconfirm,
		{
			var po = await LoadAsync(id, r.Version, token);
			po.Unconfirm(await receipts.ExistsActiveForOrderAsync(id, token));   // 草稿入库单也算下游
			return await SaveAndAuditAsync(po, Operations.PurchaseOrder.Unconfirm, null, token);
		}, ct);

	VoidAsync(Guid id, VoidDocumentRequest r, CancellationToken ct) =>
		ExecuteIdempotentAsync(r.CommandId, Operations.PurchaseOrder.Void,
		{
			var po = await LoadAsync(id, r.Version, token);
			po.Void(r.Reason, await receipts.ExistsActiveForOrderAsync(id, token), user.Name, time.GetLocalNow());
			return await SaveAndAuditAsync(po, Operations.PurchaseOrder.Void, new { r.Reason }, token);
		}, ct);

	CloseAsync(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		ExecuteIdempotentAsync(r.CommandId, Operations.PurchaseOrder.Close,
		{
			var po = await LoadAsync(id, r.Version, token);
			po.Close(user.Name, time.GetLocalNow());
			return await SaveAndAuditAsync(po, Operations.PurchaseOrder.Close, null, token);
		}, ct);

	// ===== 私有：仅去除重复，不承载业务 =====
	private async Task<PurchaseOrder> LoadAsync(Guid id, int version, CancellationToken ct)
	{
		var po = await orders.GetForUpdateAsync(id, ct) ?? throw new NotFoundException("采购单", id);
		po.EnsureVersion(version);
		return po;
	}

	SaveAndAuditAsync(PurchaseOrder po, string operation, object? detail, CancellationToken ct)
	{
		await orders.UpdateAsync(po, ct);
		await audit.WriteAsync(operation, po.Id, detail, ct);
		return po.Adapt<PurchaseOrderDto>();
	}

	private async Task EnsureReferencesActiveAsync(PurchaseOrder po, CancellationToken ct)
	{
		if (await suppliers.GetActiveAsync(po.SupplierId, ct) is null)
			throw new BusinessRuleException(ErrorCodes.InvalidStatus, "供应商已停用，不能确认。");
		var map = await products.GetManyAsync(po.Lines.Select(l => l.ProductId).Distinct(), ct);
		var inactive = po.Lines.FirstOrDefault(l => map.GetValueOrDefault(l.ProductId) is not { IsActive: true });
		if (inactive is not null)
			throw new BusinessRuleException(ErrorCodes.ProductInactive, $"第 {inactive.LineNo} 行产品已停用。");
	}
}
```

## 关于“确认预览/依据指纹”
不需要单独的预览接口和指纹：用户看到的就是 `GET` 返回的单据（含 Version），确认时提交该 Version。
确认期间若有人修改了单据，Version 不同 → 409，客户端重新加载。
如果确认需要展示额外计算（如与历史价格对比），做成普通 `GET .../confirmation-preview` 只读查询，不加锁、不开事务；提交时仍只靠 Version。

## 并发与锁
- `GetForUpdateAsync` 对单据头 `FOR UPDATE`：同一单据的命令串行化。
- `UpdateAsync` 带 `WHERE version=@Version`：双保险。
- 不需要 Serializable，不需要 advisory 锁。

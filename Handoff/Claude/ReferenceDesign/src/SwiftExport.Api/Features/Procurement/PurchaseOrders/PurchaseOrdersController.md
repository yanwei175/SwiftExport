# PurchaseOrdersController

`SwiftExport.Api/Features/Procurement/PurchaseOrders/PurchaseOrdersController.cs`

```csharp
[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController(PurchaseOrderService service, IPurchaseOrderQueries queries) : ControllerBase
{
	// ---- 查询 ----
	[HttpGet, HasPermission(Permissions.PurchaseOrders.View)]
	public Task<PagedResult<PurchaseOrderListItemDto>> Search([FromQuery] PurchaseOrderSearchRequest q, CancellationToken ct)
		=> queries.SearchAsync(q, ct);

	[HttpGet("{id:guid}"), HasPermission(Permissions.PurchaseOrders.View)]
	public async Task<PurchaseOrderDto> Get(Guid id, CancellationToken ct)
		=> await queries.GetAsync(id, ct) ?? throw new NotFoundException("采购单", id);

	// ---- 草稿 ----
	[HttpPost, HasPermission(Permissions.PurchaseOrders.Edit)]
	public Task<PurchaseOrderDto> Save(SavePurchaseOrderRequest r, CancellationToken ct) => service.SaveAsync(r, ct);

	[HttpDelete("{id:guid}"), HasPermission(Permissions.PurchaseOrders.Edit)]
	public async Task<IActionResult> Delete(Guid id, [FromQuery] int version, CancellationToken ct)
	{
		await service.DeleteAsync(id, version, ct);
		return NoContent();
	}

	// ---- 状态命令 ----
	[HttpPost("{id:guid}/confirm"), HasPermission(Permissions.PurchaseOrders.Confirm)]
	public Task<PurchaseOrderDto> Confirm(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		service.ConfirmAsync(id, r, ct);

	[HttpPost("{id:guid}/unconfirm"), HasPermission(Permissions.PurchaseOrders.Confirm)]
	public Task<PurchaseOrderDto> Unconfirm(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		service.UnconfirmAsync(id, r, ct);

	[HttpPost("{id:guid}/void"), HasPermission(Permissions.PurchaseOrders.Void)]
	public Task<PurchaseOrderDto> Void(Guid id, VoidDocumentRequest r, CancellationToken ct) =>
		service.VoidAsync(id, r, ct);

	[HttpPost("{id:guid}/close"), HasPermission(Permissions.PurchaseOrders.Close)]
	public Task<PurchaseOrderDto> Close(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		service.CloseAsync(id, r, ct);
}
```

路由规则：资源复数 kebab-case；状态命令一律 `POST {id}/{动词}`。

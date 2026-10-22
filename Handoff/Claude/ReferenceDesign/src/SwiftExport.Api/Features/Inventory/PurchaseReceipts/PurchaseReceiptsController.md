# PurchaseReceiptsController

`SwiftExport.Api/Features/Inventory/PurchaseReceipts/PurchaseReceiptsController.cs`

```csharp
[ApiController]
[Route("api/purchase-receipts")]
public sealed class PurchaseReceiptsController(PurchaseReceiptService service, IPurchaseReceiptQueries queries) : ControllerBase
{
	[HttpGet, HasPermission(Permissions.PurchaseReceipts.View)]
	public Task<PagedResult<PurchaseReceiptListItemDto>> Search([FromQuery] PurchaseReceiptSearchRequest q, CancellationToken ct)
		=> queries.SearchAsync(q, ct);

	[HttpGet("{id:guid}"), HasPermission(Permissions.PurchaseReceipts.View)]
	public async Task<PurchaseReceiptDto> Get(Guid id, CancellationToken ct)
		=> await queries.GetAsync(id, ct) ?? throw new NotFoundException("入库单", id);

	[HttpGet("{id:guid}/posting-preview"), HasPermission(Permissions.PurchaseReceipts.View)]
	public Task<PostingPreviewDto> Preview(Guid id, CancellationToken ct) => queries.GetPostingPreviewAsync(id, ct);

	[HttpPost, HasPermission(Permissions.PurchaseReceipts.Edit)]
	public Task<PurchaseReceiptDto> Save(SavePurchaseReceiptRequest r, CancellationToken ct) => service.SaveAsync(r, ct);

	[HttpDelete("{id:guid}"), HasPermission(Permissions.PurchaseReceipts.Edit)]
	public async Task<IActionResult> Delete(Guid id, [FromQuery] int version, CancellationToken ct)
	{ await service.DeleteAsync(id, version, ct); return NoContent(); }

	[HttpPost("{id:guid}/post"), HasPermission(Permissions.PurchaseReceipts.Post)]
	public Task<PurchaseReceiptDto> Post(Guid id, DocumentCommandRequest r, CancellationToken ct) =>
		service.PostAsync(id, r, ct);

	[HttpPost("{id:guid}/reverse"), HasPermission(Permissions.PurchaseReceipts.Reverse)]
	public Task<PurchaseReceiptDto> Reverse(Guid id, ReverseDocumentRequest r, CancellationToken ct) =>
		service.ReverseAsync(id, r, ct);
}
```

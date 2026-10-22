# ProductsController

`SwiftExport.Api/Features/MasterData/Products/ProductsController.cs`

```csharp
[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductService service, IProductQueries queries) : ControllerBase
{
	[HttpGet, HasPermission(Permissions.Products.View)]
	public Task<PagedResult<ProductListItemDto>> Search([FromQuery] ProductSearchRequest q, CancellationToken ct)
		=> queries.SearchAsync(q, ct);

	[HttpGet("{id:guid}"), HasPermission(Permissions.Products.View)]
	public async Task<ProductDto> Get(Guid id, CancellationToken ct)
		=> await queries.GetAsync(id, ct) ?? throw new NotFoundException("产品", id);

	[HttpPost, HasPermission(Permissions.Products.Edit)]
	[ProducesResponseType<ProductDto>(201)]
	public async Task<IActionResult> Create(CreateProductRequest r, CancellationToken ct)
	{
		var dto = await service.CreateAsync(r, ct);
		return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
	}

	[HttpPut("{id:guid}"), HasPermission(Permissions.Products.Edit)]
	public Task<ProductDto> Update(Guid id, UpdateProductRequest r, CancellationToken ct)
		=> service.UpdateAsync(id, r, ct);

	[HttpPut("{id:guid}/active"), HasPermission(Permissions.Products.Edit)]
	public Task<ProductDto> SetActive(Guid id, SetProductActiveRequest r, CancellationToken ct)
		=> service.SetActiveAsync(id, r, ct);
}
```

Controller 职责：路由、权限声明、状态码（201）。没有 try/catch，没有业务判断。

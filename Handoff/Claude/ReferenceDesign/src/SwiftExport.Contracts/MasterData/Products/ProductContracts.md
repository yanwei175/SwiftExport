# Products — Contracts

`SwiftExport.Contracts/MasterData/Products/`

```csharp
public class ProductDto
{
	public Guid Id { get; set; }
	public string Code { get; set; }
	public string Name { get; set; }
	public string Unit { get; set; }
	public string Spec { get; set; }
	public bool IsActive { get; set; }
	public int Version { get; set; }          // 客户端修改时原样回传
}

public class ProductListItemDto { public Guid Id; public string Code; public string Name; public string Unit; public bool IsActive; } // 属性形式同上

public class ProductSearchRequest : PagedRequest { public bool? IsActive { get; set; } }

public class CreateProductRequest { public string Code; public string Name; public string Unit; public string Spec; }
public class UpdateProductRequest { public string Name; public string Unit; public string Spec; public int Version; }
public class SetProductActiveRequest { public bool IsActive; public int Version; }

// Refit
public interface IProductsApi
{
	[Get("/api/products")]              Task<PagedResult<ProductListItemDto>> SearchAsync([Query] ProductSearchRequest q);
	[Get("/api/products/{id}")]         Task<ProductDto> GetAsync(Guid id);
	[Post("/api/products")]             Task<ProductDto> CreateAsync([Body] CreateProductRequest r);
	[Put("/api/products/{id}")]         Task<ProductDto> UpdateAsync(Guid id, [Body] UpdateProductRequest r);
	[Put("/api/products/{id}/active")]  Task<ProductDto> SetActiveAsync(Guid id, [Body] SetProductActiveRequest r);
}
```

说明：主数据**没有删除接口**——被单据引用后不能删，统一用停用。编码创建后不可修改（单据快照依赖编码）。

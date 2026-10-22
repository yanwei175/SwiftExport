# ProductService（写用例）

`SwiftExport.Application/MasterData/Products/ProductService.cs`

```csharp
public sealed class ProductService(IUnitOfWork uow, IProductRepository products, IAuditLog audit)
{
	public Task<ProductDto> CreateAsync(CreateProductRequest r, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			var product = Product.Create(r.Code, r.Name, r.Unit, r.Spec);
			await products.InsertAsync(product, token);   // 唯一冲突 → 仓储抛 BusinessRuleException(DuplicateCode)
			await audit.WriteAsync(Operations.Product.Create, product.Id, r, token);
			return product.Adapt<ProductDto>();
		}, ct);

	public Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest r, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			var product = await products.GetForUpdateAsync(id, token) ?? throw new NotFoundException("产品", id);
			product.EnsureVersion(r.Version);
			product.Update(r.Name, r.Unit, r.Spec);
			await products.UpdateAsync(product, token);   // WHERE version=@Version
			await audit.WriteAsync(Operations.Product.Update, id, r, token);
			return product.Adapt<ProductDto>();
		}, ct);

	public Task<ProductDto> SetActiveAsync(Guid id, SetProductActiveRequest r, CancellationToken ct) =>
		uow.ExecuteAsync(async token =>
		{
			var product = await products.GetForUpdateAsync(id, token) ?? throw new NotFoundException("产品", id);
			product.EnsureVersion(r.Version);
			product.SetActive(r.IsActive);
			await products.UpdateAsync(product, token);
			await audit.WriteAsync(r.IsActive ? Operations.Product.Activate : Operations.Product.Deactivate, id, null, token);
			return product.Adapt<ProductDto>();
		}, ct);
}
```

不使用 `ICommandExecutor`：重复提交主数据修改只会得到 409（版本已变），无害。

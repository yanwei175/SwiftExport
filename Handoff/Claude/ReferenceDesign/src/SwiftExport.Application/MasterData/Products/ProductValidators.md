# ProductValidators（FluentValidation）

`SwiftExport.Application/MasterData/Products/ProductValidators.cs`

```csharp
public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
	public CreateProductValidator()
	{
		RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9-_.]+$");
		RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
		RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
		RuleFor(x => x.Spec).MaximumLength(500);
	}
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
	public UpdateProductValidator()
	{
		RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
		RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
		RuleFor(x => x.Version).GreaterThanOrEqualTo(0);
	}
}

public sealed class ProductSearchValidator : AbstractValidator<ProductSearchRequest>
{
	public ProductSearchValidator()
	{
		RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
		RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
		RuleFor(x => x.Keyword).MaximumLength(100);
	}
}
```

校验器只做**不查库的格式校验**。“编码是否重复”需要查库 → 由数据库唯一约束保证，Service 捕获后转业务异常（见 ProductService）。
不要在校验器里注入仓储做异步查库（会产生“校验通过但插入时已重复”的竞态，最终仍要靠唯一约束）。

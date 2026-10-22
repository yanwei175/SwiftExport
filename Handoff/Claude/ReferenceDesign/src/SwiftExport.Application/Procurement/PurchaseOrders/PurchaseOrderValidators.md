# PurchaseOrderValidators

`SwiftExport.Application/Procurement/PurchaseOrders/PurchaseOrderValidators.cs`

```csharp
public sealed class SavePurchaseOrderValidator : AbstractValidator<SavePurchaseOrderRequest>
{
	public SavePurchaseOrderValidator()
	{
		RuleFor(x => x.SupplierId).NotEmpty();
		RuleFor(x => x.DocumentDate).NotEmpty();
		RuleFor(x => x.Remark).MaximumLength(500);
		RuleFor(x => x.Lines).NotEmpty().WithMessage("至少需要一行明细。");
		RuleForEach(x => x.Lines).ChildRules(l =>
		{
			l.RuleFor(x => x.ProductId).NotEmpty();
			l.RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 4, true);
			l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 4, true);
		});
		RuleFor(x => x.Lines).Must(ls => ls.Where(l => l.Id.HasValue).Select(l => l.Id).Distinct().Count()
									   == ls.Count(l => l.Id.HasValue)).WithMessage("明细行 Id 重复。");
	}
}

public sealed class VoidDocumentValidator : AbstractValidator<VoidDocumentRequest>
{
	public VoidDocumentValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
}

public sealed class PurchaseOrderSearchValidator : AbstractValidator<PurchaseOrderSearchRequest>
{
	public PurchaseOrderSearchValidator()
	{
		RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
		RuleFor(x => x).Must(x => x.DateFrom is null || x.DateTo is null || x.DateFrom <= x.DateTo);
	}
}
```

“供应商是否启用”“产品是否停用”需要查库 → 在 Service/实体中判断，不放校验器。

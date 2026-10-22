# Product（实体）

`SwiftExport.Domain/MasterData/Products/Product.cs`

```csharp
public sealed class Product : Entity
{
	public string Code { get; private set; }
	public string Name { get; private set; }
	public string Unit { get; private set; }
	public string Spec { get; private set; }
	public bool IsActive { get; private set; }

	private Product() { }   // Dapper

	public static Product Create(string code, string name, string unit, string spec) => new()
	{
		Id = Guid.CreateVersion7(), Code = code.Trim(), Name = name.Trim(),
		Unit = unit.Trim(), Spec = spec?.Trim(), IsActive = true
	};

	public void Update(string name, string unit, string spec)
	{
		Name = name.Trim(); Unit = unit.Trim(); Spec = spec?.Trim();
	}

	public void SetActive(bool active) => IsActive = active;
}
```

主数据规则很少，实体就很薄——这是正常的。不要为了“富模型”硬加方法。

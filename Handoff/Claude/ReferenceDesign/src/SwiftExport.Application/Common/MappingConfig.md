# MappingConfig（Mapster 集中配置）

```csharp
namespace SwiftExport.Application.Common;

// 在 DI 注册时调用一次：services.AddSingleton(MappingConfig.Create()); services.AddScoped<IMapper, ServiceMapper>();
public static class MappingConfig
{
	public static TypeAdapterConfig Create()
	{
		var c = new TypeAdapterConfig();
		// 同名属性无需配置；只有不同名/计算字段才写
		c.NewConfig<PurchaseOrder, PurchaseOrderDto>()
			.Map(d => d.StatusText, s => s.Status.ToString());
		c.NewConfig<PurchaseReceipt, PurchaseReceiptDto>();
		c.NewConfig<Product, ProductDto>();
		return c;
	}
}
```

规则：
- 方向只允许 **实体 → DTO**。
- **禁止 `request.Adapt<Entity>()` 或 `request.Adapt(entity)`**：会绕过实体方法和状态保护。请求 → 实体必须调用构造函数/实体方法。
- 查询直接由 Dapper 映射到 DTO，不经过实体，也不需要 Mapster。
- 每个模块可以有自己的 `XxxMappingRegister : IRegister`，放在功能文件夹内。

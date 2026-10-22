# PagedRequest / PagedResult<T>

```csharp
namespace SwiftExport.Contracts.Common;

public class PagedRequest
{
	public int Page { get; set; } = 1;        // 从 1 开始
	public int PageSize { get; set; } = 50;   // 校验器限制 1..200
	public string Keyword { get; set; }
	public string SortBy { get; set; }        // 白名单字段，由 Queries 映射到列名
	public bool Desc { get; set; }
}

public class PagedResult<T>
{
	public IList<T> Items { get; set; } = new List<T>();
	public int Total { get; set; }
	public int Page { get; set; }
	public int PageSize { get; set; }
}
```

查询专用条件类继承 `PagedRequest`，如 `PurchaseOrderSearchRequest : PagedRequest { Status?, SupplierId?, DateFrom?, DateTo? }`。

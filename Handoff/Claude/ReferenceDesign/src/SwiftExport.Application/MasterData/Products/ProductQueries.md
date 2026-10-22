# ProductQueries（读用例）

`SwiftExport.Application/MasterData/Products/ProductQueries.cs` 定义接口，`SwiftExport.Infrastructure/MasterData/Products/ProductQueries.cs` 实现。

```csharp
// Application
public interface IProductQueries
{
	Task<PagedResult<ProductListItemDto>> SearchAsync(ProductSearchRequest q, CancellationToken ct);
	Task<ProductDto?> GetAsync(Guid id, CancellationToken ct);
}

// Infrastructure —— 直接 SQL → DTO，不经过实体，不开事务
public sealed class ProductQueries(IDbSession db) : IProductQueries
{
	private static readonly Dictionary<string, string> Sortable = new(StringComparer.OrdinalIgnoreCase)
		{ ["code"] = "code", ["name"] = "name" };

	public async Task<PagedResult<ProductListItemDto>> SearchAsync(ProductSearchRequest q, CancellationToken ct)
	{
		var order = Sortable.GetValueOrDefault(q.SortBy ?? "", "code") + (q.Desc ? " DESC" : "");
		var sql = $"""
			SELECT id, code, name, unit, is_active, COUNT(*) OVER() AS total
			FROM product
			WHERE is_deleted = false
			  AND (@Keyword IS NULL OR code ILIKE '%'||@Keyword||'%' OR name ILIKE '%'||@Keyword||'%')
			  AND (@IsActive IS NULL OR is_active = @IsActive)
			ORDER BY {order}
			LIMIT @PageSize OFFSET @Skip
			""";
		var rows = (await db.Connection.QueryAsync<ProductListRow>(new CommandDefinition(sql,
			new { q.Keyword, q.IsActive, q.PageSize, Skip = (q.Page - 1) * q.PageSize }, cancellationToken: ct))).ToList();
		return new PagedResult<ProductListItemDto>
		{
			Items = rows.Adapt<List<ProductListItemDto>>(), Total = rows.FirstOrDefault()?.Total ?? 0,
			Page = q.Page, PageSize = q.PageSize
		};
	}

	public Task<ProductDto?> GetAsync(Guid id, CancellationToken ct) =>
		db.Connection.QuerySingleOrDefaultAsync<ProductDto>(new CommandDefinition(
			"SELECT id, code, name, unit, spec, is_active, version FROM product WHERE id=@id AND is_deleted=false",
			new { id }, cancellationToken: ct));
}
```

Dapper 需全局开启 `DefaultTypeMap.MatchNamesWithUnderscores = true`（snake_case ↔ PascalCase）。
查询接口放 Application、实现放 Infrastructure，是为了 Application 层不出现 SQL；查询类本身不含业务逻辑。

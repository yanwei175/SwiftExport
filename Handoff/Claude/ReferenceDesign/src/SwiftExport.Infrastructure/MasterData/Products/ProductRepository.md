# ProductRepository

`SwiftExport.Application/MasterData/Products/IProductRepository.cs`（接口）
`SwiftExport.Infrastructure/MasterData/Products/ProductRepository.cs`（实现）

```csharp
public interface IProductRepository
{
	Task<Product?> GetForUpdateAsync(Guid id, CancellationToken ct);
	Task<Product?> GetAsync(Guid id, CancellationToken ct);             // 其他用例校验引用时使用
	Task InsertAsync(Product p, CancellationToken ct);
	Task UpdateAsync(Product p, CancellationToken ct);
}

public sealed class ProductRepository(IDbSession db, ICurrentUser user) : IProductRepository
{
	public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
		db.Connection.QuerySingleOrDefaultAsync<Product>(new CommandDefinition(
			"SELECT * FROM product WHERE id=@id AND is_deleted=false FOR UPDATE", new { id }, db.Transaction, cancellationToken: ct));

	public async Task InsertAsync(Product p, CancellationToken ct)
	{
		try
		{
			await db.Connection.ExecuteAsync(new CommandDefinition("""
				INSERT INTO product(id, code, name, unit, spec, is_active, version, created_at, created_by, is_deleted)
				VALUES (@Id, @Code, @Name, @Unit, @Spec, @IsActive, 0, now(), @User, false)
				""", new { p.Id, p.Code, p.Name, p.Unit, p.Spec, p.IsActive, User = user.Name }, db.Transaction, cancellationToken: ct));
		}
		catch (Exception ex) when (PgErrors.IsUniqueViolation(ex))
		{
			throw new BusinessRuleException(ErrorCodes.DuplicateCode, $"产品编码 {p.Code} 已存在。");
		}
	}

	public async Task UpdateAsync(Product p, CancellationToken ct)
	{
		var n = await db.Connection.ExecuteAsync(new CommandDefinition("""
			UPDATE product SET name=@Name, unit=@Unit, spec=@Spec, is_active=@IsActive,
				   version=version+1, updated_at=now(), updated_by=@User
			WHERE id=@Id AND version=@Version
			""", new { p.Id, p.Name, p.Unit, p.Spec, p.IsActive, p.Version, User = user.Name }, db.Transaction, cancellationToken: ct));
		if (n != 1) throw new ConcurrencyException();
		p.Version++;
	}
}
```

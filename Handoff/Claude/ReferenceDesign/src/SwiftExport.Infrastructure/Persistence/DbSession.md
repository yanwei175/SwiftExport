# DbSession（实现 IUnitOfWork）

```csharp
namespace SwiftExport.Infrastructure.Persistence;

// Scoped：一个 HTTP 请求一个实例
public sealed class DbSession(NpgsqlDataSource dataSource) : IDbSession, IUnitOfWork, IAsyncDisposable
{
	private NpgsqlConnection? _conn;
	private NpgsqlTransaction? _tx;

	// 首次使用才打开
	public NpgsqlConnection Connection => _conn ??= dataSource.OpenConnection();
	public NpgsqlTransaction? Transaction => _tx;   // 只读，仓储传给 Dapper

	public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
	{
		if (_tx is not null) return await work(ct);   // 嵌套：由最外层提交

		_tx = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
		try
		{
			var result = await work(ct);
			try { await _tx.CommitAsync(CancellationToken.None); }  // 提交不响应取消
			catch (Exception ex) when (!PgErrors.IsDefiniteFailure(ex))
			{ throw new CommitOutcomeUnknownException(ex); }        // 503 outcome_unknown
			return result;
		}
		catch
		{
			try { await _tx.RollbackAsync(CancellationToken.None); } catch { /* 连接已断，数据库会自动回滚 */ }
			throw;                                                   // 原样抛出，不转换不记日志
		}
		finally { await _tx.DisposeAsync(); _tx = null; }
	}

	public async ValueTask DisposeAsync() { if (_conn is not null) await _conn.DisposeAsync(); }
}
```

```csharp
// Infrastructure/Persistence/PgErrors.cs —— 用 SqlState 判断，不看消息文本
static class PgErrors
{
	public static bool IsUniqueViolation(Exception e)   => e is PostgresException { SqlState: "23505" };
	public static bool IsTransient(Exception e)         => e is PostgresException { SqlState: "40001" or "40P01" or "55P03" };
	public static bool IsDefiniteFailure(Exception e)   => e is PostgresException;  // 服务器明确拒绝 = 未提交
}
```

DI：`builder.Services.AddNpgsqlDataSource(conn)`；`AddScoped<DbSession>()`，再把 `IDbSession`、`IUnitOfWork` 都指向它。

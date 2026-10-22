# Application 公共接口

```csharp
namespace SwiftExport.Application.Common;

// 事务边界。业务代码开启/提交事务的唯一入口。实现：Infrastructure/Persistence/DbSession
// 普通写入直接调用；正式命令经 ICommandExecutor（其内部调用本接口）。
// 不是编译期保证：Connection.BeginTransaction() 仍可调用，由架构测试禁止 DbSession 以外的生产代码调用数据库事务 API（决策 #30、#31）。
public interface IUnitOfWork
{
	Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
	Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct);
}

// 当前用户。实现：Infrastructure/Identity/HttpCurrentUser（读 HttpContext.User）
public interface ICurrentUser
{
	Guid Id { get; }
	string Name { get; }
}

// 业务审计。必须在 uow 事务内调用
public interface IAuditLog
{
	Task WriteAsync(string operation, Guid entityId, object? detail, CancellationToken ct); // operation 取自 Operations
}

// 单据号生成。实现用 PostgreSQL sequence：PO-20250101-0001
public interface IDocumentNumberGenerator
{
	Task<string> NextAsync(string prefix, DateTime date, CancellationToken ct);
}
```

时间统一注入 `TimeProvider`（框架自带），`time.GetLocalNow()`。

# Domain 公共：Entity / DocumentEntity / DomainException

```csharp
namespace SwiftExport.Domain.Common;

// 所有表共有字段；审计字段由仓储在 INSERT/UPDATE 时写入，不由业务代码赋值
public abstract class Entity
{
	public Guid Id { get; protected set; }
	public int Version { get; set; }              // 仓储读写，业务不改
	public DateTimeOffset CreatedAt { get; set; }
	public string CreatedBy { get; set; }
	public DateTimeOffset? UpdatedAt { get; set; }
	public string UpdatedBy { get; set; }
	public bool IsDeleted { get; protected set; }

	// 客户端提交的版本与当前不同 → 409
	public void EnsureVersion(int expected)
	{
		if (Version != expected) throw new ConcurrencyException();
	}
}

// 单据头共有字段与通用断言
public abstract class DocumentEntity<TStatus> : Entity where TStatus : struct, Enum
{
	public string DocumentNo { get; protected set; }
	public DateTime DocumentDate { get; protected set; }
	public TStatus Status { get; protected set; }
	public string Remark { get; protected set; }

	protected void RequireStatus(params TStatus[] allowed)
	{
		if (!allowed.Contains(Status))
			throw new BusinessRuleException(ErrorCodes.InvalidStatus,
				$"单据 {DocumentNo} 当前状态为 {Status}，不能执行此操作。");
	}
}
```

```csharp
// Domain/Common/Exceptions.cs —— 业务代码只抛这几种
public class BusinessRuleException(string code, string message) : Exception(message) { public string Code => code; } // 422
public class NotFoundException(string what, object id) : Exception($"{what} {id} 不存在。");                     // 404
public class ConcurrencyException() : Exception("数据已被他人修改，请刷新后重试。");                               // 409
```

规则：
- 状态字段 `protected set`，只能通过实体方法改变。
- 实体不读当前用户与时间，由调用方以参数 `(string user, DateTimeOffset now)` 传入。
- 实体不访问数据库；需要查库的规则放 Application。

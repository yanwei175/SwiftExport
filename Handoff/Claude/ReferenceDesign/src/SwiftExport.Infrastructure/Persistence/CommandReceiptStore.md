# CommandReceiptStore（幂等回执）

全系统**只有一张**幂等表：现有的 `command_receipt`（实体 `SwiftExport.Core.Entities.Commands.CommandReceipt`），**沿用，不改名、不另建表**。
不同单据靠 `command_type` 区分（取值见 `Application/Common/Operations`）；`command_id` 由客户端为每次操作新生成的 GUID，全库唯一（`UX_CommandReceipt_CommandId`）。

| 列 | 含义 |
|---|---|
（客户端生成，重试复用；随请求体 `CommandId` 传入，不用 Header）
| `actor_uid` | 首次执行的操作者 |
| `command_type` | 稳定操作名常量 |
| `request_target_uid` | 目标单据 Id；创建命令可空 |
| `fingerprint_version` / `request_fingerprint` | 规范化输入的 SHA-256（64 位十六进制）及其规范版本 |
| `result_version` / `result_json` | 首次成功结果及其结构版本 |
| `recorded_at_utc` | 写入时间 |

表为追加只写（`[DbAppendOnly]`），不更新、不软删除。

```csharp
namespace SwiftExport.Infrastructure.Persistence;

public sealed class CommandReceiptStore(IDbSession db) : ICommandReceiptStore
{
	// 回执已存在：读到后重放。
	// 首次并发同 CommandId（回执尚不存在，FOR UPDATE 锁不住不存在的行）：两个请求都会进入业务，
	// 但会在单据头 FOR UPDATE 处串行；后者因状态/版本已变得到 409/422，或在插入回执时撞唯一索引得到 409。
	// 客户端随后用同一 CommandId 重试即可读到前者的结果。
	public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken ct) =>
		db.Connection.QuerySingleOrDefaultAsync<CommandReceipt>(new CommandDefinition(
			"SELECT * FROM command_receipt WHERE command_id = @commandId FOR UPDATE",
			new { commandId }, db.Transaction, cancellationToken: ct));

	public async Task InsertAsync(CommandReceipt r, CancellationToken ct)
	{
		try
		{
			await db.Connection.ExecuteAsync(new CommandDefinition(
				"""
				INSERT INTO command_receipt(command_id, actor_uid, command_type, request_target_uid,
					fingerprint_version, request_fingerprint, result_version, result_json, recorded_at_utc)
				VALUES (@CommandId, @ActorUid, @CommandType, @RequestTargetUid,
					@FingerprintVersion, @RequestFingerprint, @ResultVersion, @ResultJson, now())
				""", r, db.Transaction, cancellationToken: ct));
		}
		catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new CommandInFlightException();   // 409，客户端用同一 CommandId 稍后重试即得到重放
		}
	}
}
```

BaseEntity 的公共列（Id、创建人等）按现有仓储惯例补齐，伪代码省略。

清理：如需清理，保留 30 天以上，由后台任务按 `recorded_at_utc` 删除；未建后台任务前不清理。

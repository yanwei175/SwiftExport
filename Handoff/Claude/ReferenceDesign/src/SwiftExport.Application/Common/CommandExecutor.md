# CommandExecutor（单据命令：事务 + 幂等）

全仓唯一的“命令管道”。只做两件事：事务、幂等。不做授权、校验、日志、异常转换。
幂等回执写入现有 `command_receipt` 表（见 `Infrastructure/Persistence/CommandReceiptStore`）。

```csharp
namespace SwiftExport.Application.Common;

public interface ICommandExecutor
{
	Task<T> ExecuteIdempotentAsync<T>(Guid commandId, string commandType, Guid? targetId, object request,
		Func<CancellationToken, Task<T>> work, CancellationToken ct);
}

public sealed class CommandExecutor(IUnitOfWork uow, ICommandReceiptStore store, ICurrentUser user)
	: ICommandExecutor
{
	private const int FingerprintVersion = 1;
	private const int ResultVersion = 1;

	public Task<T> ExecuteIdempotentAsync<T>(Guid commandId, string commandType, Guid? targetId, object request,
		Func<CancellationToken, Task<T>> work, CancellationToken ct)
	{
		if (commandId == Guid.Empty) throw new BusinessRuleException(ErrorCodes.Validation, "缺少 CommandId。");
		var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

		return uow.ExecuteAsync(async token =>
		{
			var prior = await store.FindAsync(commandId, token);
			if (prior is not null)
			{
				if (prior.CommandType != commandType || prior.ActorUid != user.Id
					|| prior.RequestTargetUid != targetId || prior.RequestFingerprint != fingerprint)
					throw new CommandIdReusedException();                 // 422
				if (prior.ResultVersion != ResultVersion)
					throw new CommandReceiptUnreadableException();        // 409，不重新执行
				return JsonSerializer.Deserialize<T>(prior.ResultJson)!;   // 重放：返回上次结果，不执行业务
			}

			var result = await work(token);

			// 唯一索引冲突（并发同 CommandId）→ CommandInFlightException(409)，整个事务回滚
			await store.InsertAsync(new CommandReceipt
			{
				CommandId = commandId, ActorUid = user.Id, CommandType = commandType, RequestTargetUid = targetId,
				FingerprintVersion = FingerprintVersion, RequestFingerprint = fingerprint,
				ResultVersion = ResultVersion, ResultJson = JsonSerializer.Serialize(result),
			}, token);
			return result;
		}, ct);
	}
}
```

放在请求体 DTO 的 `CommandId` 属性中（与现有 WPF 模型一致），**不使用 HTTP Header**。
- `commandType`：`Operations.*` 常量。
- `targetId`：目标单据 Id；创建命令传 `null`。
- `request`：只传用户意图（单据 Id、ExpectedVersion、提交的明细），不要传整个实体，也不要包含 `CommandId` 本身。
- 修改指纹规范化方式时提升 `FingerprintVersion`；修改结果结构时提升 `ResultVersion`。

使用时机：确认、取消确认、作废、关闭、过账、冲销。草稿保存与主数据不用（直接 `uow.ExecuteAsync`）。

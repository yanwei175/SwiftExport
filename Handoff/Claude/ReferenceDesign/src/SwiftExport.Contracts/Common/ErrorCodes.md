# ErrorCodes（ProblemDetails.type 的取值）

```csharp
namespace SwiftExport.Contracts.Common;

public static class ErrorCodes
{
	// 通用（由 GlobalExceptionHandler 产生）
	public const string Validation           = "validation";             // 400
	public const string NotFound             = "not_found";              // 404
	public const string ConcurrencyConflict  = "concurrency_conflict";   // 409 重新加载后再操作
	public const string CommandInFlight  = "command_in_flight";  // 409 同 CommandId 并发，后到者已回滚；用同 CommandId 重试
	public const string CommandIdReused = "command_id_reused";    // 422
	public const string Transient            = "transient";              // 503 仅限提交前失败：确定未生效，可重试
	public const string OutcomeUnknown       = "outcome_unknown";        // 503 提交本身失败：用同一 CommandId 重试
	public const string Unexpected           = "unexpected";             // 500 是否生效由事务阶段和回执判定（决策 #27）

	// 业务规则（422），按 "模块.原因" 命名，由 BusinessRuleException 携带
	public const string InvalidStatus        = "document.invalid_status";
	public const string HasDownstream        = "document.has_downstream";
	public const string ExceedsRemaining     = "receipt.exceeds_remaining";
	public const string NegativeStock        = "stock.negative";
	public const string ProductInactive      = "product.inactive";
	public const string DuplicateCode        = "master.duplicate_code";
}
```

客户端只按 `type` 分支，不解析 `detail` 文本。

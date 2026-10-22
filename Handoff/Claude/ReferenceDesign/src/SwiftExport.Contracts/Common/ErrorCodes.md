# ErrorCodes（ProblemDetails.type 的取值）

```csharp
namespace SwiftExport.Contracts.Common;

public static class ErrorCodes
{
	// 通用（由 GlobalExceptionHandler 产生）
	public const string Validation           = "validation";             // 400
	public const string NotFound             = "not_found";              // 404
	public const string ConcurrencyConflict  = "concurrency_conflict";   // 409 重新加载后再操作
	public const string CommandInFlight  = "command_in_flight";  // 409 同 CommandId 正在执行，稍后用同 CommandId 重试
	public const string CommandIdReused = "command_id_reused";
	public const string Transient            = "transient";              // 503 确定未生效，可重试
	public const string OutcomeUnknown       = "outcome_unknown";        // 503 用同一 Key 重试
	public const string Unexpected           = "unexpected";             // 500 已回滚

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

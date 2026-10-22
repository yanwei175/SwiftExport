# GlobalExceptionHandler（唯一的异常 → HTTP 转换点，唯一记录异常堆栈处）

```csharp
namespace SwiftExport.Api.Errors;

public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> log)
	: IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
	{
		var (status, type, title) = ex switch
		{
			NotFoundException               => (404, ErrorCodes.NotFound, ex.Message),
			ConcurrencyException            => (409, ErrorCodes.ConcurrencyConflict, ex.Message),
			CommandInFlightException        => (409, ErrorCodes.CommandInFlight,
			BusinessRuleException b         => (422, b.Code, b.Message),
			CommandIdReusedException        => (422, ErrorCodes.CommandIdReused, ex.Message),
			CommitOutcomeUnknownException   => (503, ErrorCodes.OutcomeUnknown, "无法确认操作是否成功，请重试。"),
			_ when PgErrors.IsTransient(ex) => (503, ErrorCodes.Transient, "数据库繁忙，操作未执行，请重试。"),
			OperationCanceledException when http.RequestAborted.IsCancellationRequested
											=> (499, "cancelled", "请求已取消。"),
			_                               => (500, ErrorCodes.Unexpected, "服务器错误，操作未执行。")
		};

		// 4xx 是预期结果：Information 不带堆栈；5xx：Error 带堆栈。全仓只在这里记录异常。
		if (status >= 500) log.LogError(ex, "请求失败 {Type}", type);
		else log.LogInformation("请求被拒绝 {Type}: {Message}", type, ex.Message);

		http.Response.StatusCode = status;
		return await problems.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = http,
			ProblemDetails = { Status = status, Type = type, Title = title,
							   Extensions = { ["traceId"] = http.TraceIdentifier } }
		});
	}
}
```

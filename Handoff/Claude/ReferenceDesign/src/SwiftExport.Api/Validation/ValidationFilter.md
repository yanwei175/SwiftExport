# ValidationFilter（FluentValidation 自动执行）

```csharp
namespace SwiftExport.Api.Validation;

// 对每个 Action 参数，若容器中存在 IValidator<T> 则执行；失败返回 400 ValidationProblemDetails
public sealed class ValidationFilter(IServiceProvider sp) : IAsyncActionFilter
{
	public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
	{
		foreach (var arg in ctx.ActionArguments.Values.Where(a => a is not null))
		{
			var validator = sp.GetService(typeof(IValidator<>).MakeGenericType(arg!.GetType())) as IValidator;
			if (validator is null) continue;
			var result = await validator.ValidateAsync(new ValidationContext<object>(arg), ctx.HttpContext.RequestAborted);
			if (!result.IsValid)
			{
				ctx.Result = new BadRequestObjectResult(new ValidationProblemDetails(result.ToDictionary())
					{ Type = ErrorCodes.Validation });
				return;
			}
		}
		await next();
	}
}
```

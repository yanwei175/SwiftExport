using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftExport.Application.Commands.Validation;

/// <summary>按真实命令类型取得必需的验证器，顺序执行异步请求验证。</summary>
public sealed class CommandRequestValidator(IServiceProvider services)
{
    /// <summary>验证请求并传递取消标识；缺失验证器属于服务配置错误。</summary>
    public async Task ValidateAsync<TRequest>(TRequest? request, CancellationToken token)
    {
        if (request is null) throw new CommandValidationException("请求不能为空。", null);
        var validator = services.GetRequiredService<IValidator<TRequest>>();
        var result = await validator.ValidateAsync(request, token);
        var first = result.Errors.FirstOrDefault();
        if (first is not null) throw new CommandValidationException(first.ErrorMessage, first.PropertyName);
    }
}

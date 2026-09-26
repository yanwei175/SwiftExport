using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>阻止 MVC 在 Action 执行前缓冲 multipart 表单，使 Controller 能直接顺序读取原始请求体。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    /// <summary>移除表单值和文件值提供器；路由、查询和 Header 绑定保持不变。</summary>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    /// <summary>资源执行完成后无需恢复本请求的值提供器集合。</summary>
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}

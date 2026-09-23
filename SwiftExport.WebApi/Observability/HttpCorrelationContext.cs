using SwiftExport.Core.Interfaces.Auditing;

namespace SwiftExport.WebApi.Observability;

/// <summary>向业务层提供当前 HTTP 请求的关联标识；没有请求时返回空字符串。</summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId => accessor.HttpContext?.TraceIdentifier ?? string.Empty;
}

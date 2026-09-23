using SwiftExport.Core.Interfaces.Auditing;

namespace SwiftExport.WebApi.Services;

public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId => accessor.HttpContext?.TraceIdentifier ?? string.Empty;
}

namespace SwiftExport.Core.Interfaces.Auditing;

/// <summary>当前请求关联标识抽象，Application 不直接依赖 HttpContext。</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

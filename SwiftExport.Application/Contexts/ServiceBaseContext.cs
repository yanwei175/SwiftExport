using Microsoft.Extensions.Logging;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Contexts;

/// <summary>
/// 聚合业务层所需会话契约及日志工厂，不依赖数据库提供者实现。
/// </summary>
public class ServiceBaseContext(IDbSession session, ILoggerFactory loggerFactory)
{
    /// <summary>与所有仓储共享的请求级会话，最外层业务用例负责事务。</summary>
    public IDbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}

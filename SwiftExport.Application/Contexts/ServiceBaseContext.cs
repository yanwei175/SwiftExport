using Microsoft.Extensions.Logging;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Contexts;

/// <summary>
/// 业务服务层基础依赖聚合
/// </summary>
public class ServiceBaseContext(DbSession session, ILoggerFactory loggerFactory)
{
    public DbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
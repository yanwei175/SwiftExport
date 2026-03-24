using Microsoft.Extensions.Logging;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Contexts;

/// <summary>
/// 仓储层基础依赖聚合
/// </summary>
public class RepositoryBaseContext(DbSession session, ILoggerFactory loggerFactory)
{
    public DbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    // 未来这里可以加 public IMemoryCache Cache { get; }
    // 💡未来这里可以加 public ICurrentUser CurrentUser { get; } (用于自动写入 CreateBy/UpdateBy)
}
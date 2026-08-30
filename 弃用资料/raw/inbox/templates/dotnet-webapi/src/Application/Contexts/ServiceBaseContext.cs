using Microsoft.Extensions.Logging;
using {{ProjectName}}.Infrastructure.Data;

namespace {{ProjectName}}.Application.Contexts;

/// <summary>
/// 业务服务层基础依赖聚合。默认 WebAPI 模板允许 Application Service 使用 scoped DbSession。
/// </summary>
public class ServiceBaseContext(DbSession session, ILoggerFactory loggerFactory)
{
    public DbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}

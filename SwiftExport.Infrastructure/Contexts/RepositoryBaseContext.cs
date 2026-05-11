using Microsoft.Extensions.Logging;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Contexts;

/// <summary>
/// 仓储层基础依赖聚合，包含 DB Session、日志、审计打戳服务。
/// </summary>
public class RepositoryBaseContext(DbSession session, ILoggerFactory loggerFactory, IAuditStampService auditStamp)
{
    public DbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;

    /// <summary>审计字段自动打戳，仓储层在 Add/Update 前调用</summary>
    public IAuditStampService AuditStamp { get; } = auditStamp;
}
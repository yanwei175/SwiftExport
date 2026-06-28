using Microsoft.Extensions.Logging;
using {{ProjectName}}.Core.Interfaces.Auth;
using {{ProjectName}}.Infrastructure.Data;

namespace {{ProjectName}}.Infrastructure.Contexts;

public class RepositoryBaseContext(DbSession session, ILoggerFactory loggerFactory, IAuditStampService auditStamp)
{
    public DbSession Session { get; } = session;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public IAuditStampService AuditStamp { get; } = auditStamp;
}

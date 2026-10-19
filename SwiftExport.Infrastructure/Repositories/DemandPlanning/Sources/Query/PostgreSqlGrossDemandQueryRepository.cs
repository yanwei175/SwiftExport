using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>使用既有会话读取 PostgreSQL 成品需求本体、分组及详情，不读取下游追踪或接管事务。</summary>
/// <param name="context">外层拥有的连接、事务与仓储基础依赖。</param>
public sealed partial class PostgreSqlGrossDemandQueryRepository(RepositoryBaseContext context) : IGrossDemandQueryRepository
{
}

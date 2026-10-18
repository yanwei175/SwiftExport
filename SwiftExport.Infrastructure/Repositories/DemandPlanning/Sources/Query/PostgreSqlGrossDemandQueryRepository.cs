using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>使用既有会话完整读取 PostgreSQL 成品需求工作台、分组、详情和追踪；保留各查询阶段，不接管事务。</summary>
/// <param name="context">外层拥有的连接、事务与仓储基础依赖。</param>
public sealed partial class PostgreSqlGrossDemandQueryRepository(RepositoryBaseContext context) : IGrossDemandQueryRepository
{
}

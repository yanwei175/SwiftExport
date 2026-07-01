using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 净需求执行关系仓储实现。
/// </summary>
public class DemandExecutionLinkRepository(RepositoryBaseContext context)
    : DapperRepository<DemandExecutionLink>(context), IDemandExecutionLinkRepository
{
}

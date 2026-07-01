using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 出库单行仓储实现。
/// </summary>
public class OutboundLineRepository(RepositoryBaseContext context)
    : DapperRepository<OutboundLine>(context), IOutboundLineRepository
{
}

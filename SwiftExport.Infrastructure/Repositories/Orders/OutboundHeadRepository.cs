using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 出库单头仓储实现。
/// </summary>
public class OutboundHeadRepository(RepositoryBaseContext context)
    : DapperRepository<OutboundHead>(context), IOutboundHeadRepository
{
}

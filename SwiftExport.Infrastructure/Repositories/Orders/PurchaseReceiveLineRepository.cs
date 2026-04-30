using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 采购入库行仓储实现。
/// </summary>
public class PurchaseReceiveLineRepository(RepositoryBaseContext context)
    : DapperRepository<PurchaseReceiveLine>(context), IPurchaseReceiveLineRepository
{
}

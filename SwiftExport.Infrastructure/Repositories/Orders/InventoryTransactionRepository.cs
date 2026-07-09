using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    /// <summary>
    /// 库存流水仓储实现。
    /// 说明：通过组合通用 DapperRepository，仅暴露 Create / AddRange，保证流水只增不改。
    /// </summary>
    public class InventoryTransactionRepository(RepositoryBaseContext context)
        : DapperRepository<InventoryTransaction>(context), IInventoryTransactionRepository
    {


    }
}

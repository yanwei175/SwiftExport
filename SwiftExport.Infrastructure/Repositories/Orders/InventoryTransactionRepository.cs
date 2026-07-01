using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    /// <summary>
    /// 库存流水仓储实现。
    /// 说明：通过组合通用 DapperRepository，仅暴露 Add / AddRange，保证流水只增不改。
    /// </summary>
    public class InventoryTransactionRepository(RepositoryBaseContext context)
        : DapperRepository<InventoryTransaction>(context), IInventoryTransactionRepository
    {
        /// <summary>
        /// 新增一条库存流水（只增不改）。
        /// </summary>
        public override Task<Guid> AddAsync(InventoryTransaction entity) => base.AddAsync(entity);

        /// <summary>
        /// 批量新增库存流水（只增不改）。
        /// </summary>
        public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<InventoryTransaction> entities)
        {
            return AddRangeCoreAsync(entities);
        }
    }
}

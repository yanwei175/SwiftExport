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
    public class InventoryTransactionRepository(RepositoryBaseContext context) : IInventoryTransactionRepository
    {
        private readonly DapperRepository<InventoryTransaction> _repo = new(context);

        /// <summary>
        /// 新增一条库存流水（只增不改）。
        /// </summary>
        public Task<Guid> AddAsync(InventoryTransaction entity)
        {
            return _repo.AddAsync(entity);
        }

        /// <summary>
        /// 批量新增库存流水（只增不改）。
        /// </summary>
        public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<InventoryTransaction> entities)
        {
            return _repo.AddRangeAsync(entities);
        }
    }
}

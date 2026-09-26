using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory.Warehouses;

/// <summary>在调用方事务中锁定仓库、核验资格并执行虚拟仓管理写入。</summary>
public interface IWarehouseManagementRepository
{
    /// <summary>在当前事务内锁定并读取待变更虚拟仓。</summary>
    Task<WarehouseSite?> GetForUpdateAsync(Guid uid);
    /// <summary>读取合法活动供应商的权威编码和名称。</summary>
    Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default);
    /// <summary>按代码锁定未删除仓库。</summary>
    Task<WarehouseSite?> FindByCodeForUpdateAsync(string code);
    /// <summary>按供应商锁定活动虚拟仓。</summary>
    Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId);
    /// <summary>新增虚拟仓，唯一冲突返回零。</summary>
    Task<int> InsertAsync(WarehouseSite site);
    /// <summary>按版本更新名称。</summary>
    Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion);
    /// <summary>按版本停用仓库。</summary>
    Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion);
    /// <summary>按版本启用仓库。</summary>
    Task<int> ActivateAsync(WarehouseSite site, int expectedVersion);
    /// <summary>按版本软删仓库。</summary>
    Task<int> DeleteAsync(Guid uid, int expectedVersion);
    /// <summary>锁定并读取阻止软删的业务引用类别。</summary>
    Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default);
    /// <summary>判断仓库是否已有非零余额或历史流水。</summary>
    Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId);
}

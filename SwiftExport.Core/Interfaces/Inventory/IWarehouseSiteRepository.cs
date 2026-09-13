using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>定义供应商虚拟仓主数据查询、并发写入和删除引用检查的持久化边界。</summary>
public interface IWarehouseSiteRepository
{
    /// <summary>按仓库管理用例分页查询活动供应商候选。</summary>
    Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页查询未删除虚拟仓及供应商显示资料。</summary>
    Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取虚拟仓显示 DTO。</summary>
    Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>在当前事务内锁定并读取待变更虚拟仓。</summary>
    Task<WarehouseSite?> GetForUpdateAsync(Guid uid);
    /// <summary>锁定同一命令标识，确保首次执行和重放判断不会并发穿透。</summary>
    Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default);
    /// <summary>读取合法活动供应商的权威编码和名称，用于建仓及快照。</summary>
    Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default);
    /// <summary>按代码锁定未删除仓库，用于在写入前给出明确唯一性冲突。</summary>
    Task<WarehouseSite?> FindByCodeForUpdateAsync(string code);
    /// <summary>按供应商锁定活动虚拟仓，用于在写入前保护一供应商一活动仓。</summary>
    Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId);
    /// <summary>新增虚拟仓；数据库唯一冲突返回零。</summary>
    Task<int> InsertAsync(WarehouseSite site);
    /// <summary>按版本更新虚拟仓名称并递增版本。</summary>
    Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion);
    /// <summary>按版本停用虚拟仓并递增版本。</summary>
    Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion);
    /// <summary>软删虚拟仓并递增并发版本；删除资格由同事务内专用检查负责。</summary>
    Task<int> DeleteAsync(Guid uid, int expectedVersion);
    /// <summary>返回阻止删除的当前库存、历史流水和业务引用类别；空集合表示可删除。</summary>
    Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default);
    /// <summary>判断虚拟仓是否存在非零余额或任何历史流水。</summary>
    Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId);
}

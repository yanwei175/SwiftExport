using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>定义库存查询与供应商虚拟仓业务命令的应用服务边界。</summary>
public interface IInventoryService
{
    /// <summary>按库存访问权限分页读取虚拟仓管理可选择的活动供应商。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchWarehouseSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页查询未删除虚拟仓，并支持供应商和活动状态筛选。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>按稳定标识读取虚拟仓当前资料。</summary>
    Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>为活动供应商创建唯一活动虚拟仓。</summary>
    Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default);
    /// <summary>按乐观并发版本修改虚拟仓名称。</summary>
    Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    /// <summary>在不存在有效库存或历史流水时停用虚拟仓。</summary>
    Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    /// <summary>软删除虚拟仓；仅允许不存在余额与流水时删除。</summary>
    Task<ServiceResult<WarehouseSiteDto>> DeleteWarehouseSiteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<InventoryPostingResultDto>>> PreviewOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<InventoryPostingResultDto>>> PostOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default);
}

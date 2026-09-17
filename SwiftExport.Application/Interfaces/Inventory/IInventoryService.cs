using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>定义库存查询与供应商虚拟仓业务命令的应用服务边界。</summary>
public interface IInventoryService
{
    /// <summary>按库存读取权限分页取得活动仓库选择候选。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
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
    /// <summary>按乐观并发版本重新启用未删除虚拟仓，并保持供应商活动仓唯一。</summary>
    Task<ServiceResult<WarehouseSiteDto>> ActivateWarehouseSiteAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    /// <summary>软删除虚拟仓；仅允许不存在余额与流水时删除。</summary>
    Task<ServiceResult<WarehouseSiteDto>> DeleteWarehouseSiteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    /// <summary>按供应商、虚拟仓、关键词和非零条件分页查询余额。</summary>
    Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>按日期、类型、来源单号和库存身份分页查询不可变流水。</summary>
    Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>分页读取指定余额的流水 Trace 与只读对账结果。</summary>
    Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
}

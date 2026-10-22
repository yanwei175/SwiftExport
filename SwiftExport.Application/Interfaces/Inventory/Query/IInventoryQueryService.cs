using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Query;

/// <summary>提供余额、不可变流水、余额追溯及库存仓库和供应商筛选候选的只读应用边界。</summary>
public interface IInventoryQueryService
{
    /// <summary>按库存查看权限分页读取未软删供应商，包含停用且不要求已有库存记录。</summary>
    Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync(PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>按库存查看权限分页读取活动仓库筛选候选，不授予仓库管理权限。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync(PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>按供应商、虚拟仓、关键词和非零条件分页查询余额。</summary>
    Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>按日期、类型、来源单号和库存身份分页查询不可变流水。</summary>
    Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>分页读取指定余额的流水 Trace 与只读对账结果。</summary>
    Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
}

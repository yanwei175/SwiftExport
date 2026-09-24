using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Warehouses;

/// <summary>提供仓库管理资料、活动供应商与仓库选择项的只读应用边界。</summary>
public interface IWarehouseQueryService
{
    /// <summary>按库存读取权限分页取得活动仓库选择候选。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>按库存访问权限分页读取虚拟仓管理可选择的活动供应商。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchWarehouseSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>分页查询未删除虚拟仓，并支持供应商和活动状态筛选。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>按稳定标识读取虚拟仓当前资料。</summary>
    Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default);
}

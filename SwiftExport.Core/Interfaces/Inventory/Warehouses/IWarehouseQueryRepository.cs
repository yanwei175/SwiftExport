using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory.Warehouses;

/// <summary>只读查询供应商虚拟仓和选择候选。</summary>
public interface IWarehouseQueryRepository
{
    /// <summary>分页读取活动、未删除的仓库选择候选。</summary>
    Task<PagedResponseBase<WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页读取活动供应商候选。</summary>
    Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页查询未删除虚拟仓。</summary>
    Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取虚拟仓显示资料。</summary>
    Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default);
}

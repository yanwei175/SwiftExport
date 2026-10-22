using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExport.Core.Interfaces.Inventory.Warehouses;

/// <summary>只读查询供应商虚拟仓和选择候选。</summary>
public interface IWarehouseQueryRepository
{
    /// <summary>在调用方事务内批量读取未删除仓库，用于产品关联校验。</summary>
    Task<IReadOnlyList<WarehouseSite>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
    /// <summary>仅投影产品维护可选择的活动仓库身份和名称，不携带库存或供应商管理资料。</summary>
    Task<PagedResponseBase<ProductDefaultWarehouseCandidateDto>> GetProductDefaultCandidatesAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页读取活动、未删除的仓库选择候选。</summary>
    Task<PagedResponseBase<WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页读取活动供应商候选。</summary>
    Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>读取库存及入库筛选用供应商身份、编码和名称；包含停用，排除软删，不依赖业务记录。</summary>
    Task<PagedResponseBase<InventorySupplierLookupDto>> SearchSupplierFilterOptionsAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
    /// <summary>分页查询未删除虚拟仓。</summary>
    Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取虚拟仓显示资料。</summary>
    Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default);
}

using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供入库管理列表、单据详情与仓库筛选候选的只读用例。</summary>
public interface IInboundQueryService
{
    /// <summary>按入库查看权限分页读取活动仓库候选，不授予仓库管理权限。</summary>
    Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync(PagedRequestBase request, CancellationToken token = default);

    /// <summary>按查询条件分页读取入库管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default);
    /// <summary>按单据身份读取完整入库详情。</summary>
    Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default);
}

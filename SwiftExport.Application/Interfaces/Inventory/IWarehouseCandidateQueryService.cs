using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>读取采购新增选择所需的已有仓库，不创建仓库、不决定收货或消耗目标。</summary>
public interface IWarehouseCandidateQueryService
{
    /// <summary>按关键词分页读取合法供应商虚拟仓；要求采购创建或确认权限，失败抛明确异常。</summary>
    Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(PagedRequestBase request, CancellationToken cancellationToken = default);
}

using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>采购仓库候选只读端口，与仓库管理查询分离资格，不提供写入操作。</summary>
public interface IWarehouseCandidateRepository
{
    /// <summary>在当前事务重验有效账号及 Admin、采购创建或确认角色。</summary>
    Task<bool> HasPermissionAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>按稳定顺序返回合法候选及总数；不读取采购、BOM、余额或历史单据。</summary>
    Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(PagedRequestBase request, CancellationToken cancellationToken);
}

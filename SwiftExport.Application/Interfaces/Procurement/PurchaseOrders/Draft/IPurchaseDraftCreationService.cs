using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>按来源独立提交需求转采购草稿的创建边界。</summary>
public interface IPurchaseDraftCreationService
{
    /// <summary>按来源事务创建或追加采购草稿，允许不同来源分别成功或失败。</summary>
    Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default);
}

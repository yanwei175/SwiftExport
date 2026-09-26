using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>需求转采购的只读预览边界。</summary>
public interface IPurchaseDraftPreviewService
{
    /// <summary>依据当前物料和可追加目标生成采购草稿预览，不写业务事实。</summary>
    Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default);
}

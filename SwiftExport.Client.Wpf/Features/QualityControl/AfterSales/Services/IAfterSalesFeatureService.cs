using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>协调售后保存后确认的连续调用，不持有任何页面状态。</summary>
public interface IAfterSalesFeatureService
{
    /// <summary>先保存事实，成功后使用最新版本确认实物需求。</summary>
    Task<AfterSalesSaveAndConfirmResult> SaveAndConfirmAsync(
        SaveAfterSalesCommand saveCommand,
        bool create,
        Guid confirmCommandId,
        CancellationToken cancellationToken = default);
}

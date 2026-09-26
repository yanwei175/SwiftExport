using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>区分“已保存”与“随后确认”的两阶段结果，避免页面误报整体回滚。</summary>
public sealed class AfterSalesSaveAndConfirmResult
{
    /// <summary>保存阶段结果。</summary>
    public required ServiceResult<AfterSalesCommandResult> Save { get; init; }
    /// <summary>确认阶段结果；保存失败时为空。</summary>
    public ServiceResult<AfterSalesCommandResult>? Confirm { get; init; }
}

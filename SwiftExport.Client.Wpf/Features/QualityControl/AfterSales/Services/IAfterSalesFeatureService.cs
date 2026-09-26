using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>分别发送售后保存与确认请求；阶段身份和重试次序由页面实例持有。</summary>
public interface IAfterSalesFeatureService
{
    /// <summary>按原有保存幂等协议发送完整保存请求，不接管确认阶段。</summary>
    Task<SwiftExport.Contracts.Wrappers.ServiceResult<AfterSalesCommandResult>> SaveAsync(
        SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default);

    /// <summary>使用已确定的保存版本发送独立确认命令，不重复保存。</summary>
    Task<SwiftExport.Contracts.Wrappers.ServiceResult<AfterSalesCommandResult>> ConfirmAsync(
        Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default);
}

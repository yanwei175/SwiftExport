using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>按页面指定的阶段分别发送售后保存或确认请求，不生成命令身份。</summary>
public sealed class AfterSalesFeatureService(IAfterSalesApi api, IApiCallRunner runner) : IAfterSalesFeatureService
{
    /// <inheritdoc/>
    public Task<SwiftExport.Contracts.Wrappers.ServiceResult<AfterSalesCommandResult>> SaveAsync(
        SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default)
        => runner.RunAsync(
            () => create ? api.CreateAsync(command, cancellationToken) : api.SaveAsync(command.Uid, command, cancellationToken),
            cancellationToken, "保存售后服务单失败");

    /// <inheritdoc/>
    public Task<SwiftExport.Contracts.Wrappers.ServiceResult<AfterSalesCommandResult>> ConfirmAsync(
        Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default)
        => runner.RunAsync(() => api.ConfirmDemandAsync(uid, command, cancellationToken),
            cancellationToken, "确认实物需求失败");
}

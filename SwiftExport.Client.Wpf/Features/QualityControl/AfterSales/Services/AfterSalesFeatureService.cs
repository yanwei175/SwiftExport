using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>执行售后保存后确认的两阶段 API 协调，明确保留每阶段结果。</summary>
public sealed class AfterSalesFeatureService(IAfterSalesApi api, IApiCallRunner runner) : IAfterSalesFeatureService
{
    /// <inheritdoc/>
    public async Task<AfterSalesSaveAndConfirmResult> SaveAndConfirmAsync(
        SaveAfterSalesCommand saveCommand,
        bool create,
        Guid confirmCommandId,
        CancellationToken cancellationToken = default)
    {
        var saved = await runner.RunAsync(
            () => create ? api.CreateAsync(saveCommand, cancellationToken) : api.SaveAsync(saveCommand.Uid, saveCommand, cancellationToken),
            cancellationToken, "保存售后服务单失败");
        if (!saved.Success || saved.Data is null)
            return new AfterSalesSaveAndConfirmResult { Save = saved };
        var confirmed = await runner.RunAsync(
            () => api.ConfirmDemandAsync(saveCommand.Uid, new ConfirmAfterSalesDemandCommand
            {
                CommandId = confirmCommandId, Version = saved.Data.Version
            }, cancellationToken), cancellationToken, "确认实物需求失败");
        return new AfterSalesSaveAndConfirmResult { Save = saved, Confirm = confirmed };
    }
}

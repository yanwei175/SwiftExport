using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Receipts;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;
namespace SwiftExport.Application.Services.OrderTracking.Receipts;
/// <summary>独立加载本区域；只核对基础需求存在，不调用整包 Trace。</summary>
public sealed class DemandTrackingReceiptService(ServiceBaseContext context,IDemandTrackingHeaderRepository headers,IDemandTrackingReceiptRepository repository) : IDemandTrackingReceiptService
{
    /// <summary>不存在、取消及技术失败保持独立语义，已核实的业务异常由区域证据说明。</summary>
    public async Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetReceiptsAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(grossDemandId==Guid.Empty) return ServiceResult<DemandTrackingReceiptSectionDto>.Fail(ServiceErrorCodes.Validation,"成品需求身份不能为空。","GrossDemandId");
        try
        {
            if(await headers.GetDemandHeaderAsync(grossDemandId,cancellationToken) is null)
                return ServiceResult<DemandTrackingReceiptSectionDto>.Fail(ServiceErrorCodes.NotFound,"未找到成品需求。");
            return ServiceResult<DemandTrackingReceiptSectionDto>.Ok(await repository.GetReceiptsAsync(grossDemandId,cancellationToken));
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<DemandTrackingReceiptService>().LogError(ex,"读取跟踪区域 Receipts 失败，需求 {GrossDemandId}。",grossDemandId);
            return ServiceResult<DemandTrackingReceiptSectionDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取跟踪区域失败，请稍后重试。");
        }
    }
}

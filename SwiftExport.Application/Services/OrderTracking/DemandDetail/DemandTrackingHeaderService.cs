using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;

namespace SwiftExport.Application.Services.OrderTracking.DemandDetail;

/// <summary>独立读取成品需求基础明细，保留历史空值，不联动下游区域。</summary>
public sealed class DemandTrackingHeaderService(ServiceBaseContext context, IDemandTrackingHeaderRepository repository) : IDemandTrackingHeaderService
{
    /// <summary>拒绝空身份，未找到与数据库不可用使用不同结构化错误。</summary>
    public async Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (grossDemandId == Guid.Empty)
            return ServiceResult<DemandTrackingHeaderDto>.Fail(ServiceErrorCodes.Validation, "成品需求身份不能为空。", "GrossDemandId");
        try
        {
            var result = await repository.GetDemandHeaderAsync(grossDemandId, cancellationToken);
            return result is null
                ? ServiceResult<DemandTrackingHeaderDto>.Fail(ServiceErrorCodes.NotFound, "未找到成品需求。")
                : ServiceResult<DemandTrackingHeaderDto>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<DemandTrackingHeaderService>().LogError(ex, "读取成品需求跟踪基础明细失败，需求 {GrossDemandId}。", grossDemandId);
            return ServiceResult<DemandTrackingHeaderDto>.Fail(ServiceErrorCodes.ServiceUnavailable, "读取成品需求基础明细失败，请稍后重试。");
        }
    }
}

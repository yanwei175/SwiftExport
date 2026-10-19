using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.MaterialDetail;
namespace SwiftExport.Application.Services.OrderTracking.MaterialDetail;
/// <summary>历史物料基础详情独立加载，不受收货历史校验影响。</summary>
public sealed class MaterialTrackingDetailService(ServiceBaseContext context,IMaterialTrackingDetailRepository repository) : IMaterialTrackingDetailService
{
    /// <summary>校验身份并区分不存在、取消和技术失败。</summary>
    public async Task<ServiceResult<MaterialTrackingDetailDto>> GetMaterialDetailAsync(Guid netDemandId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(netDemandId==Guid.Empty)return ServiceResult<MaterialTrackingDetailDto>.Fail(ServiceErrorCodes.Validation,"物料需求身份不能为空。","NetDemandId");
        try
        {
            var result=await repository.GetMaterialDetailAsync(netDemandId,cancellationToken);
            return result is null?ServiceResult<MaterialTrackingDetailDto>.Fail(ServiceErrorCodes.NotFound,"未找到物料需求。"):ServiceResult<MaterialTrackingDetailDto>.Ok(result);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<MaterialTrackingDetailService>().LogError(ex,"读取物料追踪详情失败，物料 {NetDemandId}。",netDemandId);
            return ServiceResult<MaterialTrackingDetailDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取物料详情失败，请稍后重试。");
        }
    }
}

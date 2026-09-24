using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;

/// <summary>定义手工来源需求的创建、修改和关闭用例。</summary>
public interface IManualGrossDemandService
{
    /// <summary>创建没有上游单据的手工来源需求。</summary>
    Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request);
    /// <summary>修改活动手工来源需求。</summary>
    Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request);
    /// <summary>关闭活动手工来源需求并保留历史。</summary>
    Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command);
}

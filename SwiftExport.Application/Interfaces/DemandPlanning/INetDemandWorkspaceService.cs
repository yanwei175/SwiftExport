using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

public interface INetDemandWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(ReplaceNetDemandPlanCommand command);
    Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(ApplyProductDefaultSupplierCommand command);
    Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(BatchAssignSupplierCommand command);
}

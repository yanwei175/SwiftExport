using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

public interface IGrossDemandWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<GrossDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<GrossDemandDecomposeResult>> DecomposeAsync(GrossDemandDecomposeCommand command);
    Task<ServiceResult<GrossDemandRollbackResult>> RollbackAsync(GrossDemandRollbackCommand command);
    Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request);
    Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request);
    Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command);
}

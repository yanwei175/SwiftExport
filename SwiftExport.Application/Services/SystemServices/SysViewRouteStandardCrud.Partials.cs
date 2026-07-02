using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.SystemServices;

public partial class SysViewRouteService
{
    public Task<ServiceResult<SysViewRouteDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SysViewRouteDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SysViewRouteDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SysViewRouteDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysViewRouteDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
namespace SwiftExport.Application.Interfaces;

public partial interface ISysDictDataService
{
    Task<ServiceResult<SysDictDataDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SysDictDataDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SysDictDataDto dto);
    Task<ServiceResult<int>> UpdateAsync(SysDictDataDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq);

    Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode);
}

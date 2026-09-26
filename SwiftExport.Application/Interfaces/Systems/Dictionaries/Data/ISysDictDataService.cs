using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
namespace SwiftExport.Application.Interfaces.Systems.Dictionaries.Data;

public partial interface ISysDictDataService : IBaseService<SysDictData, SysDictDataDto>
{

    Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SysDictDataDto>>> GetPagedAsync(SysDictDataSearchRequest request);
    Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq);
    Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode);
}

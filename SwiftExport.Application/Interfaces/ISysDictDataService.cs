using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

public partial interface ISysDictDataService
{
    Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode);
}

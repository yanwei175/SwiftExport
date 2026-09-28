using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;

namespace SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

public interface ISysDictTypeRepository : IRepository<SysDictType>
{
    Task<PagedResponseBase<SysDictTypeDto>> GetPagedAsync(SysDictTypeSearchRequest request);

    Task<IReadOnlyList<SysDictType>> GetAllForUpdateAsync();

    Task<IReadOnlyList<SysDictType>> GetByDictCodesAsync(IEnumerable<string> dictCodes, bool withUpdLock = false);

}

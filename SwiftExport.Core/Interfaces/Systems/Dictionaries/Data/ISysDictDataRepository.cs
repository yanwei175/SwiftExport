using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Interfaces.Systems.Dictionaries.Data
{
    /// <summary>
    /// 字典明细仓储接口
    /// </summary>
    public interface ISysDictDataRepository : IRepository<SysDictData>
    {
        Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request);

        Task<IReadOnlyList<SysDictData>> GetByCodeAsync(string dictCode);

        Task<IReadOnlyList<string>> GetDictCodesWithDataAsync(IEnumerable<string> dictCodes);
    }
}

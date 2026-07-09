using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// ×ÖµäÃ÷Ï¸²Ö´¢½Ó¿Ú
    /// </summary>
    public interface ISysDictDataRepository : IRepository<SysDictData>
    {
        Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request);
    }
}

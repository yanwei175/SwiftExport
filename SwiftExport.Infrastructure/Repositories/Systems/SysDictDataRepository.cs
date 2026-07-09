using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems
{
    /// <summary>
    /// ×ÖµäÃ÷Ï¸²Ö´¢ÊµÏÖ
    /// </summary>
    public class SysDictDataRepository(RepositoryBaseContext context)
        : DapperRepository<SysDictData>(context), ISysDictDataRepository
    {
        public Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request)
        {
            throw new NotImplementedException();
        }
    }
}

using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    public class SysDictDataService(IRepository<SysDictData> repository, ServiceBaseContext context)
            : BaseService<SysDictData, SysDictDataDto>(repository, context), ISysDictDataService
    {
        // 软删除开关：如果你字典表确实用了 IsDeleted 字段，就保持默认 true。
        // protected override bool IsSoftDelete => true; 
    }
}

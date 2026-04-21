using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 字典明细服务
    /// 继承 BaseService 提供标准 CRUD / BatchSave 能力。
    /// </summary>
    public class SysDictDataService(ISysDictDataRepository repository, ServiceBaseContext context)
            : BaseService<SysDictData, SysDictDataDto>(repository, context), ISysDictDataService
    {
        /// <summary>
        /// 字典明细使用软删除。
        /// </summary>
        protected override bool IsSoftDelete => true;
    }
}

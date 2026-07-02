using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 字典明细服务。
    /// </summary>
    public partial class SysDictDataService(ISysDictDataRepository repository, ServiceBaseContext context)
            : ApplicationServiceBase(context), ISysDictDataService
    {
        private readonly ServiceCrud<SysDictData, SysDictDataDto> _crud = new(repository, context);
    }
}

using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    public class SysDictTypeService(IRepository<SysDictType> repository, ServiceBaseContext context)
            : BaseService<SysDictType, DictTypeDto>(repository, context), ISysDictTypeService
    {


        // 🚀 你可以在这里重写钩子函数
        // 比如：删除字典大类前，校验下面是不是还有明细数据
        protected override async Task<string?> ValidateBeforeDeleteAsync(List<int> ids)
        {
            // var hasData = await _dataRepository.ExistsAsync(...);
            // if (hasData) return "该字典分类下还有明细数据，禁止删除！";
            return await base.ValidateBeforeDeleteAsync(ids);
        }
    }
}

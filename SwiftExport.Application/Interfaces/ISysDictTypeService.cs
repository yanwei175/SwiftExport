using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 系统字典类型服务接口
    /// </summary>
    public partial interface ISysDictTypeService
    {
        /// <summary>
        /// 获取字典类型分组树
        /// 按 GroupCode 分组，返回树形结构（一层分组树）
        /// </summary>
        /// <returns>分组树节点列表</returns>
        Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync();
    }
}

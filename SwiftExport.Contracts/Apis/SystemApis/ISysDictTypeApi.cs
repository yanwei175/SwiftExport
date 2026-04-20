using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using Refit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 系统字典类型 API 接口
    /// </summary>
    public interface ISysDictTypeApi : IGenericApi<SysDictTypeDto>
    {
        /// <summary>
        /// 获取字典类型分组树
        /// 返回按 GroupCode 分组的树形结构
        /// 路由对应：[HttpGet("group-tree")] 在 [Route("api/[controller]")] 下
        /// 实际路由：GET /api/sysdicttype/group-tree
        /// </summary>
        [Get("/group-tree")]
        Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync();
    }
}

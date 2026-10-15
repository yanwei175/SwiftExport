using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 系统字典类型 API 接口
    /// </summary>
    public interface ISysDictTypeApi : IGenericApi<SysDictTypeDto>
    {
        /// <summary>显式补齐全部内置字典类型与默认值；保留数据库已有身份和显示配置，返回新增及修复数量。</summary>
        [Post("/sync-built-in")]
        Task<ServiceResult<BuiltInDictionarySyncResponse>> SyncBuiltInAsync(CancellationToken cancellationToken = default);

        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync();

        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>> GetPagedAsync([Body] SysDictTypeSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SysDictTypeBatchSaveResponse>> BatchSaveAsync([Body] SysDictTypeBatchSaveRequest request);

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

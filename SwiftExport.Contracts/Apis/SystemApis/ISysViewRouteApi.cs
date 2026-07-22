using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 视图路由配置 API 接口（Refit 客户端）
    /// 继承通用 CRUD，额外暴露"获取所有启用路由"接口，供前端启动时批量拉取缓存。
    /// </summary>
    public interface ISysViewRouteApi : IGenericApi<SysViewRouteDto>
    {
        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAllAsync();

        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SysViewRouteDto>>> GetPagedAsync([Body] SysViewRouteSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SysViewRouteBatchSaveResponse>> BatchSaveAsync([Body] SysViewRouteBatchSaveRequest request);

        /// <summary>
        /// 获取所有启用的路由配置（IsEnabled=true）。
        /// 前端应用启动时调用一次，结果缓存在 ViewRouteService 中。
        /// </summary>
        [Get("/enabled")]
        Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync();
    }
}

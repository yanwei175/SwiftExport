using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SystemEntities;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 视图路由配置服务接口
    /// 继承通用 CRUD，扩展"获取所有启用路由"方法。
    /// </summary>
    public partial interface ISysViewRouteService
    {
    Task<ServiceResult<SysViewRouteDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SysViewRouteDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SysViewRouteDto dto);
    Task<ServiceResult<int>> UpdateAsync(SysViewRouteDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysViewRouteDto> batchReq);

        /// <summary>
        /// 获取所有启用的路由配置（IsEnabled=true, IsDeleted=false）。
        /// </summary>
        Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync();
    }
}

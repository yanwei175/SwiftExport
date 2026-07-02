using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Systems
{
    /// <summary>
    /// 视图路由配置控制器
    /// 此处额外暴露 /enabled 接口，供前端启动时批量拉取路由缓存。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SysViewRouteController(ISysViewRouteService service)
        : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<ServiceResult<SysViewRouteDto>> GetById(Guid id) => await service.GetByIdAsync(id);

        [HttpPost]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<Guid>> Create([FromBody] SysViewRouteDto dto) => await service.AddAsync(dto);

        [HttpPut("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SysViewRouteDto dto) => await service.UpdateAsync(dto);

        [HttpDelete("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

        [HttpPost("page")]
        public async Task<ServiceResult<PagedResultDto<SysViewRouteDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
            => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

        [HttpPost("batch-save")]
        [PreventDuplicateRequest(WindowSeconds = 5)]
        public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<SysViewRouteDto> req) => await service.BatchSaveAsync(req);

        [HttpGet("all")]
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAll()
        {
            return await service.GetAllAsync();
        }

        /// <summary>
        /// 获取所有启用的路由配置（供前端缓存使用）。
        /// GET /api/SysViewRoute/enabled
        /// </summary>
        [HttpGet("enabled")]
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutes()
        {
            return await service.GetEnabledRoutesAsync();
        }
    }
}

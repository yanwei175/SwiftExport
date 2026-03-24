using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.WebApi.Controllers.Base // 你的实际命名空间
{
    // ★ 1. 必须是 abstract，防止 ASP.NET 把它当成一个真实的 Controller 跑去注册
    // ★ 2. 路由标在这里，所有子类自动继承这个规则！
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseController<TEntity, TDto>(IBaseService<TEntity, TDto> service) : ControllerBase where TEntity : BaseEntity where TDto : class
    {
        // 保护级别的服务接口，子类可以直接用
        protected readonly IBaseService<TEntity, TDto> _service = service;

        // ================= 标准 RESTful 接口 =================

        [HttpGet("{id}")]
        public virtual async Task<ServiceResult<TDto>> GetById(int id)
        {
            return await _service.GetByIdAsync(id);
        }

        [HttpPost]
        public virtual async Task<ServiceResult<int>> Create([FromBody] TDto dto)
        {
            return await _service.AddAsync(dto);
        }

        [HttpPut("{id}")]
        public virtual async Task<ServiceResult<int>> Update(int id, [FromBody] TDto dto)
        {
            // 你可以在基类里做一些统一拦截，比如 ID 校验
            return await _service.UpdateAsync(dto);
        }

        [HttpDelete("{id}")]
        public virtual async Task<ServiceResult<int>> Delete(int id)
        {
            return await _service.DeleteAsync(id);
        }


        [HttpPost("page")]
        public virtual async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        {
            return await _service.GetPagedListAsync(criteria, pageIndex, pageSize);
        }


        [HttpPost("batch-save")]
        public virtual async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestGeneric<TDto> req)
        {
            // 直接调用泛型服务里的批量事务处理
            return await ((IBaseService<TEntity, TDto>)_service).BatchSaveAsync(req);
        }
    }
}
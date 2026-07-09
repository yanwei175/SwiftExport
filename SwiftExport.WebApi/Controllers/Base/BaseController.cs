using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Base;

// ★ 1. 必须是 abstract，防止 ASP.NET 把它当成一个真实的 Controller 跑去注册
// ★ 2. 路由标在这里，所有子类自动继承这个规则！
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<TEntity, TDto>(IBaseService<TEntity, TDto> service) : ControllerBase where TEntity : BaseEntity where TDto : class
{        // 保护级别的服务接口，子类可以直接用
    protected readonly IBaseService<TEntity, TDto> _service = service;
    [HttpGet("{id}")]
    public virtual async Task<ServiceResult<TDto>> GetById(Guid id)
    {
        return await _service.GetByIdAsync(id);
    }

    [HttpPost]
    [PreventDuplicateRequest] // 中文注释：防抖式防重复提交，默认 3 秒冷却窗口。
    public virtual async Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<TDto> dto)
    {
        return await _service.AddAsync(dto);
    }

    [HttpPut("{id}")]
    [PreventDuplicateRequest] // 中文注释：防抖式防重复提交，同接口 3 秒冷却。
    public virtual async Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<TDto> dto)
    {
        // 你可以在基类里做一些统一拦截，比如 ID 校验
        return await _service.UpdateAsync(dto);
    }

    [HttpDelete("{id}")]
    [PreventDuplicateRequest] // 中文注释：防抖式防重复提交，防止重复删除。
    public virtual async Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id)
    {
        return await _service.DeleteAsync(id);
    }


}

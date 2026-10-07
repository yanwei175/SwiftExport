using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Base;

/// <summary>提供实体 CRUD 的 HTTP 边界，更新时拒绝路径与请求体身份冲突。</summary>
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

    /// <summary>核对路径与外层请求身份后委托更新，身份冲突不调用写入服务。</summary>
    [HttpPut("{id}")]
    [PreventDuplicateRequest] // 中文注释：防抖式防重复提交，同接口 3 秒冷却。
    public virtual async Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<TDto> dto)
    {
        if (id != dto.Uid)
            return ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.Validation, "路径 Id 与请求体 Uid 不一致。", "Uid");
        return await _service.UpdateAsync(dto);
    }

    [HttpDelete("{id}")]
    [PreventDuplicateRequest] // 中文注释：防抖式防重复提交，防止重复删除。
    public virtual async Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id)
    {
        return await _service.DeleteAsync(id);
    }


}

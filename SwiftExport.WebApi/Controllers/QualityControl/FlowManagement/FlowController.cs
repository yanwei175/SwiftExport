using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.QualityControl.FlowManagement;

/// <summary>流量测试组 HTTP 边界；只进行身份绑定和服务转发，不开放独立行写入。</summary>
[ApiController]
[Authorize]
[Route("api/quality-control/flows")]
public sealed class FlowController(IFlowService service) : ControllerBase
{
    /// <summary>按物料、历史 SKU 和测试日期分页查询。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync(
        [FromBody] FlowSearchRequest request, CancellationToken cancellationToken) => service.GetPagedAsync(request, cancellationToken);

    /// <summary>读取一个未删除测试组的完整历史数据。</summary>
    [HttpGet("{id:guid}")]
    public Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken) => service.GetByIdAsync(id, cancellationToken);

    /// <summary>新增包含全部功能行的测试组。</summary>
    [HttpPost]
    public Task<ServiceResult<FlowSaveResponse>> CreateAsync(
        [FromBody] CreateItemRequest<FlowEditDto> request, CancellationToken cancellationToken) => service.CreateAsync(request, cancellationToken);

    /// <summary>拒绝路径与请求身份不一致的更新，再转发完整组保存。</summary>
    [HttpPut("{id:guid}")]
    public Task<ServiceResult<FlowSaveResponse>> UpdateAsync(Guid id,
        [FromBody] UpdateItemRequest<FlowEditDto> request, CancellationToken cancellationToken) =>
        request is null || id != request.Uid
            ? Task.FromResult(ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.Validation, "路径 Id 与请求体 Uid 不一致。", "Uid"))
            : service.UpdateAsync(request, cancellationToken);

    /// <summary>核对路径身份后按版本整体软删除。</summary>
    [HttpDelete("{id:guid}")]
    public Task<ServiceResult<SaveItemResult>> DeleteAsync(Guid id,
        [FromBody] DeleteItemRequest request, CancellationToken cancellationToken) =>
        request is null || id != request.Uid
            ? Task.FromResult(ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.Validation, "路径 Id 与请求体 Uid 不一致。", "Uid"))
            : service.DeleteAsync(request, cancellationToken);
}

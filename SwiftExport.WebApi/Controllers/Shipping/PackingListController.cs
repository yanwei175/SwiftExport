using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.WebApi.Controllers.Shipping;

/// <summary>装箱单查询及草稿命令 HTTP 边界；不开放库存过账或反向入口。</summary>
[ApiController]
[Authorize]
[Route("api/shipping/packing-lists")]
public sealed class PackingListController(
    IPackingListService service,
    ICurrentUser currentUser,
    IUserPermissionService permissions) : ControllerBase
{
    /// <summary>分页读取装箱单管理摘要。</summary>
    [HttpPost("search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>> SearchAsync(
        [FromBody] PackingListManagerSearchRequest request, CancellationToken token) =>
        AuthorizedAsync(() => service.SearchAsync(request, token), token);

    /// <summary>读取装箱单完整详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ActionResult<ServiceResult<PackingListDetailDto>>> GetAsync(Guid uid, CancellationToken token) =>
        AuthorizedAsync(() => service.GetAsync(uid, token), token);

    /// <summary>分页读取同客户仍可交付的 SO/CR 来源。</summary>
    [HttpPost("sources/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>>> SearchSourcesAsync(
        [FromBody] PackingListSourceSearchRequest request, CancellationToken token) =>
        AuthorizedAsync(() => service.SearchSourcesAsync(request, token), token);

    /// <summary>原子创建草稿；命令自身提供持久化幂等，不使用短时请求冷却。</summary>
    [HttpPost("drafts")]
    public Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>> CreateAsync(
        [FromBody] CreatePackingListDraftCommand command, CancellationToken token) =>
        AuthorizedAsync(() => service.CreateAsync(command, token), token);

    /// <summary>按头行版本和显式删除集合原子更新草稿。</summary>
    [HttpPut("drafts/{uid:guid}")]
    public Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>> UpdateAsync(
        Guid uid, [FromBody] UpdatePackingListDraftCommand command, CancellationToken token) =>
        AuthorizedAsync(() => service.UpdateAsync(uid, command, token), token);

    /// <summary>按版本软删除活动未过账草稿。</summary>
    [HttpPost("drafts/{uid:guid}/delete")]
    public Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>> DeleteAsync(
        Guid uid, [FromBody] DeletePackingListDraftCommand command, CancellationToken token) =>
        AuthorizedAsync(() => service.DeleteAsync(uid, command, token), token);

    /// <summary>在 HTTP 边界返回真实 403；Application 仍保留同一权限校验供非 HTTP 调用。</summary>
    private async Task<ActionResult<ServiceResult<T>>> AuthorizedAsync<T>(
        Func<Task<ServiceResult<T>>> action, CancellationToken token)
    {
        if (!currentUser.UserId.HasValue
            || !await permissions.HasPermissionAsync(currentUser.UserId.Value, PermissionKeys.ModuleShippingAccess, token))
            return Forbid();
        return await action();
    }
}

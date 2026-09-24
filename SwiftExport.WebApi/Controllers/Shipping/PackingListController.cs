using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.WebApi.Controllers.Shipping;

/// <summary>装箱单草稿、过账、整单反向及追踪 HTTP 边界；库存事务由 Application 编排。</summary>
[ApiController]
[Authorize]
[Route("api/shipping/packing-lists")]
public sealed class PackingListController(
    IPackingListService service,
    IPackingListTraceService trace,
    IPackingListPostingService posting,
    ICurrentUser currentUser,
    IUserPermissionService permissions) : ControllerBase
{
    /// <summary>箱单post-preview业务边界；稳定命令由服务持久化去重，不使用短时冷却。</summary>
    [HttpPost("{uid:guid}/post-preview")]
    public Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>> PostPreviewAsync(Guid uid, [FromBody] PackingListPostPreviewRequest request, CancellationToken token) =>
        AuthorizedAsync(() => posting.PreviewAsync(uid, request, token), token, PermissionKeys.PackingListPost);

    /// <summary>箱单post业务边界；稳定命令由服务持久化去重，不使用短时冷却。</summary>
    [HttpPost("{uid:guid}/post")]
    public Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>> PostAsync(Guid uid, [FromBody] PostPackingListCommand request, CancellationToken token) =>
        AuthorizedAsync(() => posting.PostAsync(uid, request, token), token, PermissionKeys.PackingListPost);

    /// <summary>箱单reverse-preview业务边界；稳定命令由服务持久化去重，不使用短时冷却。</summary>
    [HttpPost("{uid:guid}/reverse-preview")]
    public Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>> ReversePreviewAsync(Guid uid, [FromBody] PackingListReversePreviewRequest request, CancellationToken token) =>
        AuthorizedAsync(() => posting.ReversePreviewAsync(uid, request, token), token, PermissionKeys.PackingListReverse);

    /// <summary>箱单reverse业务边界；稳定命令由服务持久化去重，不使用短时冷却。</summary>
    [HttpPost("{uid:guid}/reverse")]
    public Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>> ReverseAsync(Guid uid, [FromBody] ReversePackingListCommand request, CancellationToken token) =>
        AuthorizedAsync(() => posting.ReverseAsync(uid, request, token), token, PermissionKeys.PackingListReverse);
    /// <summary>读取箱单实际执行证据，缺少证据时不生成推测结果。</summary>
    [HttpGet("{uid:guid}/trace")]
    public Task<ActionResult<ServiceResult<PackingListTraceDto>>> TraceAsync(Guid uid, CancellationToken token) =>
        AuthorizedAsync(() => trace.GetAsync(uid, token), token);

    /// <summary>分页读取装箱单管理摘要。</summary>
    [HttpPost("search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>> SearchAsync(
        [FromBody] PackingListManagerSearchRequest request, CancellationToken token) =>
        AuthorizedAsync(() => service.SearchAsync(request, token), token);

    /// <summary>读取装箱单完整详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ActionResult<ServiceResult<PackingListDetailDto>>> GetAsync(Guid uid, CancellationToken token) =>
        AuthorizedAsync(() => service.GetAsync(uid, token), token);

    /// <summary>分页读取同客户仍可交付的 GrossDemand 来源。</summary>
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
        Func<Task<ServiceResult<T>>> action, CancellationToken token, string? operationPermission = null)
    {
        if (!currentUser.UserId.HasValue
            || !await permissions.HasPermissionAsync(currentUser.UserId.Value, PermissionKeys.ModuleShippingAccess, token))
            return Forbid();
        if (operationPermission != null
            && !await permissions.HasPermissionAsync(currentUser.UserId.Value, operationPermission, token))
            return Forbid();
        return await action();
    }
}

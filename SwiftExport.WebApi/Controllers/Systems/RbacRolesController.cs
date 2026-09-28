using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>提供普通角色及角色权限整组维护；内置管理员角色不可经此接口修改。</summary>
[ApiController]
[Authorize]
[Route("api/system/roles")]
public sealed class RbacRolesController(IRbacManagementService service) : ControllerBase
{
    /// <summary>分页查询角色，包含停用角色。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>> Search([FromBody] ManagedPageRequest request, CancellationToken cancellationToken) =>
        service.SearchRolesAsync(request, cancellationToken);

    /// <summary>读取角色详情与聚合版本。</summary>
    [HttpGet("{id:guid}")]
    public Task<ServiceResult<ManagedRoleDto>> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetRoleAsync(id, cancellationToken);

    /// <summary>以不可复用的稳定编码创建普通角色。</summary>
    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ManagedRoleDto>> Create([FromBody] CreateManagedRoleRequest request, CancellationToken cancellationToken) =>
        service.CreateRoleAsync(request, cancellationToken);

    /// <summary>按预期版本修改普通角色的显示资料。</summary>
    [HttpPut("{id:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ManagedRoleDto>> Update(Guid id, [FromBody] UpdateManagedRoleRequest request, CancellationToken cancellationToken) =>
        service.UpdateRoleAsync(id, request, cancellationToken);

    /// <summary>按预期版本启停普通角色。</summary>
    [HttpPut("{id:guid}/status")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ManagedRoleDto>> SetStatus(Guid id, [FromBody] SetManagedStatusRequest request, CancellationToken cancellationToken) =>
        service.SetRoleStatusAsync(id, request, cancellationToken);

    /// <summary>删除无用户关联的普通角色，须提供预期版本。</summary>
    [HttpDelete("{id:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<bool>> Delete(Guid id, [FromQuery] int expectedVersion, CancellationToken cancellationToken) =>
        service.DeleteRoleAsync(id, expectedVersion, cancellationToken);

    /// <summary>读取角色权限；内置管理员返回自动全目录说明。</summary>
    [HttpGet("{id:guid}/permissions")]
    public Task<ServiceResult<RolePermissionsDto>> GetPermissions(Guid id, CancellationToken cancellationToken) =>
        service.GetRolePermissionsAsync(id, cancellationToken);

    /// <summary>在事务内按角色版本整组替换普通角色权限。</summary>
    [HttpPut("{id:guid}/permissions")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<RolePermissionsDto>> ReplacePermissions(Guid id, [FromBody] ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        service.ReplaceRolePermissionsAsync(id, request, cancellationToken);
}

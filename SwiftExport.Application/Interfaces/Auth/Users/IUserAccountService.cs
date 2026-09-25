using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth.Users;

/// <summary>定义账号创建、本人改密、管理员重置及管理用户选择入口。</summary>
public interface IUserAccountService
{
    /// <summary>由当前真实 Admin 创建无角色的新用户。</summary>
    Task<ServiceResult<Guid>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>校验旧密码后修改本人密码。</summary>
    Task<ServiceResult<bool>> ChangeOwnPasswordAsync(ChangePasswordRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>由当前真实 Admin 重置非 Admin 用户密码。</summary>
    Task<ServiceResult<bool>> ResetUserPasswordAsync(AdminResetPasswordRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>仅向真实 Admin 返回权限维护目标列表。</summary>
    Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync(CancellationToken cancellationToken = default);
}

using Refit;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SwiftExport.Contracts.Apis.AuthApis
{
    /// <summary>
    /// 认证相关 Refit 接口：登录、当前用户及账号密码管理。
    /// </summary>
    public interface IAuthApi
    {
        /// <summary>
        /// POST /api/auth/login
        /// 不需要 Bearer Token，成功返回 access_token。
        /// </summary>
        [Post("/api/auth/login")]
        Task<LoginTokenDto> LoginAsync([Body] LoginRequestDto request);

        /// <summary>
        /// GET /api/auth/me
        /// 需要 Bearer Token，返回当前用户信息。
        /// </summary>
        [Get("/api/auth/me")]
        Task<MeDto> GetMeAsync();

        /// <summary>
        /// POST /api/auth/register
        /// 需要管理员 Bearer Token。
        /// </summary>
        [Post("/api/auth/register")]
        Task<ServiceResult<System.Guid>> RegisterAsync([Body] RegisterRequestDto request);

        /// <summary>
        /// POST /api/auth/change-password
        /// 当前用户验证旧密码后修改自己的密码。
        /// </summary>
        [Post("/api/auth/change-password")]
        Task<ServiceResult<bool>> ChangePasswordAsync([Body] ChangePasswordRequestDto request);

        /// <summary>
        /// POST /api/auth/admin/reset-password
        /// 仅管理员可重置非管理员用户密码。
        /// </summary>
        [Post("/api/auth/admin/reset-password")]
        Task<ServiceResult<bool>> ResetUserPasswordAsync([Body] AdminResetPasswordRequestDto request);

        /// <summary>返回管理员可维护的最小用户列表。</summary>
        [Get("/api/auth/admin/users")]
        Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync();

    }
}

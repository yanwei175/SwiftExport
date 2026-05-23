using Refit;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.AuthApis
{
    /// <summary>
    /// 认证相关 Refit 接口：登录、查询当前用户、注册。
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
        Task<ServiceResult<int>> RegisterAsync([Body] RegisterRequestDto request);

        /// <summary>
        /// POST /api/auth/reset-password
        /// 匿名重置密码，校验码需为任一管理员账号密码。
        /// </summary>
        [Post("/api/auth/reset-password")]
        Task<ResetPasswordResultDto> ResetPasswordAsync([Body] ResetPasswordRequestDto request);
    }
}

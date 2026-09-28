using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Authentication;

public interface IAuthService
{
    /// <summary>登录并获取服务端完整身份与权限快照。</summary>
    Task<ServiceResult<MeDto>> LoginAsync(string userName, string password);

    /// <summary>尝试以已缓存令牌恢复当前会话。</summary>
    Task<bool> TryRestoreSessionAsync();

    /// <summary>显式刷新当前用户权限，过期会话响应不得覆盖新会话。</summary>
    Task<ServiceResult<MeDto>> RefreshCurrentUserAsync();
}

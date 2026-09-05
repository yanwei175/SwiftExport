using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Authentication;

/// <summary>提供当前客户端会话的用户身份、角色展示和稳定权限键查询。</summary>
public interface ICurrentUserService
{
    /// <summary>当前用户或其权限快照发生替换、清除时触发。</summary>
    event EventHandler? CurrentUserChanged;

    bool IsAuthenticated { get; }

    MeDto? CurrentUser { get; }

    string DisplayName { get; }

    IReadOnlyList<string> Roles { get; }

    /// <summary>获取当前用户由 /api/auth/me 返回的稳定权限键。</summary>
    IReadOnlyList<string> Permissions { get; }

    bool IsAdmin { get; }

    /// <summary>判断当前用户是否拥有指定稳定权限键；不使用角色名称兜底。</summary>
    bool HasPermission(string permissionKey);

    /// <summary>用认证接口返回的完整身份与权限快照替换当前用户。</summary>
    void SetCurrentUser(MeDto user);

    /// <summary>清除当前身份、角色及权限快照。</summary>
    void Clear();
}

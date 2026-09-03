using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Authentication;

/// <summary>提供当前客户端会话的用户身份、角色展示和稳定权限键查询。</summary>
public interface ICurrentUserService
{
    bool IsAuthenticated { get; }

    MeDto? CurrentUser { get; }

    string DisplayName { get; }

    IReadOnlyList<string> Roles { get; }

    /// <summary>获取当前用户由 /api/auth/me 返回的稳定权限键。</summary>
    IReadOnlyList<string> Permissions { get; }

    bool IsAdmin { get; }

    /// <summary>判断当前用户是否拥有指定稳定权限键；不使用角色名称兜底。</summary>
    bool HasPermission(string permissionKey);

    void SetCurrentUser(MeDto user);

    void Clear();
}

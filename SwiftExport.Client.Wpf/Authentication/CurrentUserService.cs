using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Authentication;

/// <summary>保存当前会话用户，并以 /api/auth/me 的 Permissions 作为客户端权限体验来源。</summary>
public sealed class CurrentUserService : ICurrentUserService
{
    public bool IsAuthenticated => CurrentUser is not null;

    public MeDto? CurrentUser { get; private set; }

    public string DisplayName
    {
        get
        {
            if (CurrentUser is null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(CurrentUser.NickName)
                ? CurrentUser.UserName
                : CurrentUser.NickName;
        }
    }

    public IReadOnlyList<string> Roles => CurrentUser?.Roles ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Permissions => CurrentUser?.Permissions ?? [];

    public bool IsAdmin => CurrentUser?.IsAdmin == true;

    /// <inheritdoc/>
    public bool HasPermission(string permissionKey)
    {
        return !string.IsNullOrWhiteSpace(permissionKey)
            && Permissions.Contains(permissionKey, StringComparer.Ordinal);
    }

    public void SetCurrentUser(MeDto user)
    {
        CurrentUser = user;
    }

    public void Clear()
    {
        CurrentUser = null;
    }
}

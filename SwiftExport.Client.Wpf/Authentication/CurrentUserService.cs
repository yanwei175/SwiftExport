using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Authentication;

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

    public bool IsAdmin => CurrentUser?.IsAdmin == true;

    public void SetCurrentUser(MeDto user)
    {
        CurrentUser = user;
    }

    public void Clear()
    {
        CurrentUser = null;
    }
}

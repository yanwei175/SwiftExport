using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Authentication;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }

    MeDto? CurrentUser { get; }

    string DisplayName { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsAdmin { get; }

    void SetCurrentUser(MeDto user);

    void Clear();
}

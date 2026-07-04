using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Authentication;

public interface IAuthService
{
    Task<ServiceResult<MeDto>> LoginAsync(string userName, string password);

    Task<bool> TryRestoreSessionAsync();
}

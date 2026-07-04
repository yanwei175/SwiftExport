using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Authentication;

public sealed class AuthService(
    IAuthApi authApi,
    IApiCallRunner apiCallRunner,
    ITokenStore tokenStore,
    ICurrentUserService currentUserService,
    ISessionService sessionService) : IAuthService
{
    public async Task<ServiceResult<MeDto>> LoginAsync(string userName, string password)
    {
        sessionService.ClearSession();

        var tokenResult = await apiCallRunner.RunValueAsync(
            () => authApi.LoginAsync(new LoginRequestDto
            {
                UserName = userName.Trim(),
                Password = password
            }),
            failureMessage: "登录失败");

        if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Data?.Access_token))
        {
            return ServiceResult<MeDto>.Fail(
                string.IsNullOrWhiteSpace(tokenResult.Message)
                    ? "登录失败：服务器未返回 Token。"
                    : tokenResult.Message);
        }

        tokenStore.SetAccessToken(tokenResult.Data.Access_token);

        var userResult = await LoadCurrentUserAsync("登录失败");
        if (!userResult.Success)
        {
            sessionService.ClearSession();
        }

        return userResult;
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!tokenStore.HasAccessToken)
        {
            return false;
        }

        var result = await LoadCurrentUserAsync("登录状态验证失败");
        if (result.Success)
        {
            return true;
        }

        sessionService.ClearSession();
        return false;
    }

    private async Task<ServiceResult<MeDto>> LoadCurrentUserAsync(string failureMessage)
    {
        var result = await apiCallRunner.RunValueAsync(
            authApi.GetMeAsync,
            failureMessage: failureMessage);

        if (!result.Success || result.Data is null)
        {
            return ServiceResult<MeDto>.Fail(
                string.IsNullOrWhiteSpace(result.Message)
                    ? failureMessage
                    : result.Message);
        }

        currentUserService.SetCurrentUser(result.Data);
        return ServiceResult<MeDto>.Ok(result.Data, "登录成功");
    }
}

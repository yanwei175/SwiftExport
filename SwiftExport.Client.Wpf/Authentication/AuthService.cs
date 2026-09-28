using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Authentication;

/// <summary>建立、恢复和刷新客户端会话，以服务端 me 作为权限快照唯一来源。</summary>
public sealed class AuthService(
    IAuthApi authApi,
    IApiCallRunner apiCallRunner,
    ITokenStore tokenStore,
    ICurrentUserService currentUserService,
    ISessionService sessionService) : IAuthService
{
    private long _sessionGeneration;

    /// <summary>登录后读取身份；较晚完成的旧登录不得覆盖后续会话。</summary>
    public async Task<ServiceResult<MeDto>> LoginAsync(string userName, string password)
    {
        var generation = Interlocked.Increment(ref _sessionGeneration);
        sessionService.ClearSession();

        var tokenResult = await apiCallRunner.RunValueAsync(
            () => authApi.LoginAsync(new LoginRequestDto
            {
                UserName = userName.Trim(),
                Password = password
            }),
            failureMessage: "登录失败");

        if (generation != Volatile.Read(ref _sessionGeneration))
            return ServiceResult<MeDto>.Fail("登录会话已更新，请重新操作。");
        if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Data?.Access_token))
        {
            return ServiceResult<MeDto>.Fail(
                string.IsNullOrWhiteSpace(tokenResult.Message)
                    ? "登录失败：服务器未返回 Token。"
                    : tokenResult.Message);
        }

        tokenStore.SetAccessToken(tokenResult.Data.Access_token);

        var userResult = await LoadCurrentUserAsync("登录失败", tokenResult.Data.Access_token, generation);
        if (!userResult.Success && generation == Volatile.Read(ref _sessionGeneration))
        {
            sessionService.ClearSession();
        }

        return userResult;
    }

    /// <summary>已有令牌恢复时只接纳同一令牌与同一代会话的响应。</summary>
    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!tokenStore.HasAccessToken)
        {
            return false;
        }

        var generation = Volatile.Read(ref _sessionGeneration);
        var token = tokenStore.GetAccessToken();
        var result = await LoadCurrentUserAsync("登录状态验证失败", token, generation);
        if (result.Success)
        {
            return true;
        }

        if (generation == Volatile.Read(ref _sessionGeneration)
            && string.Equals(token, tokenStore.GetAccessToken(), StringComparison.Ordinal)) sessionService.ClearSession();
        return false;
    }

    /// <summary>用户主动刷新权限；刷新失败保留当前快照并如实返回错误。</summary>
    public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync()
    {
        var token = tokenStore.GetAccessToken();
        if (string.IsNullOrWhiteSpace(token) || !currentUserService.IsAuthenticated)
            return Task.FromResult(ServiceResult<MeDto>.Fail("当前没有可刷新的登录会话。"));
        return LoadCurrentUserAsync("刷新权限失败", token, Volatile.Read(ref _sessionGeneration),
            currentUserService.CurrentUser?.UserId);
    }

    /// <summary>只在令牌、会话代数及预期用户仍一致时接纳 me 响应。</summary>
    private async Task<ServiceResult<MeDto>> LoadCurrentUserAsync(string failureMessage, string? expectedToken,
        long generation, string? expectedUserId = null)
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

        if (generation != Volatile.Read(ref _sessionGeneration)
            || !string.Equals(expectedToken, tokenStore.GetAccessToken(), StringComparison.Ordinal)
            || (expectedUserId is not null && expectedUserId != currentUserService.CurrentUser?.UserId)
            || (expectedUserId is not null && expectedUserId != result.Data.UserId))
            return ServiceResult<MeDto>.Fail("登录会话已切换，旧请求结果已忽略。");
        currentUserService.SetCurrentUser(result.Data);
        return ServiceResult<MeDto>.Ok(result.Data, "登录成功");
    }
}

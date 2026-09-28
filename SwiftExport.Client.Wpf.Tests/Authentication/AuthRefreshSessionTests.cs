using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Authentication;

/// <summary>验证权限显式刷新只接纳当前令牌及当前账号的服务端身份结果。</summary>
[TestClass]
public sealed class AuthRefreshSessionTests
{
    /// <summary>会话切换后，旧 me 响应不得写回新账号权限。</summary>
    [TestMethod]
    public async Task DelayedRefresh_DoesNotOverwriteNewSession()
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        var source = new MeSource();
        var api = DispatchProxy.Create<IAuthApi, AuthApiProxy>();
        ((AuthApiProxy)(object)api).Source = source;
        var tokens = new MemoryTokenStore();
        tokens.SetAccessToken("first-token");
        var current = new CurrentUserService();
        current.SetCurrentUser(new MeDto { UserId = firstId, Permissions = ["Suppliers.Supplier.View"] });
        var service = new AuthService(api, new RunnerStub(), tokens, current, new SessionStub(tokens, current));

        var pending = service.RefreshCurrentUserAsync();
        tokens.SetAccessToken("second-token");
        current.SetCurrentUser(new MeDto { UserId = secondId, Permissions = ["Products.Product.View"] });
        source.Complete(new MeDto { UserId = firstId, Permissions = ["System.Users.Manage"] });
        var result = await pending;

        Assert.IsFalse(result.Success);
        Assert.AreEqual(secondId, current.CurrentUser?.UserId);
        CollectionAssert.AreEqual(new[] { "Products.Product.View" }, current.Permissions.ToArray());
    }

    /// <summary>同一账号显式刷新会替换权限并发布快照变化通知。</summary>
    [TestMethod]
    public async Task RefreshCurrentUser_UpdatesPermissionSnapshot()
    {
        var userId = Guid.NewGuid().ToString();
        var source = new MeSource();
        var api = DispatchProxy.Create<IAuthApi, AuthApiProxy>();
        ((AuthApiProxy)(object)api).Source = source;
        var tokens = new MemoryTokenStore();
        tokens.SetAccessToken("same-token");
        var current = new CurrentUserService();
        current.SetCurrentUser(new MeDto { UserId = userId, Permissions = ["Suppliers.Supplier.View"] });
        var changeCount = 0;
        current.CurrentUserChanged += (_, _) => changeCount++;
        var service = new AuthService(api, new RunnerStub(), tokens, current, new SessionStub(tokens, current));

        var pending = service.RefreshCurrentUserAsync();
        source.Complete(new MeDto { UserId = userId, Permissions = ["Products.Product.View"] });
        var result = await pending;

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, changeCount);
        Assert.IsFalse(current.HasPermission("Suppliers.Supplier.View"));
        Assert.IsTrue(current.HasPermission("Products.Product.View"));
    }

    /// <summary>用受控完成时间模拟 Refit 的 me 响应。</summary>
    public sealed class MeSource
    {
        private readonly TaskCompletionSource<MeDto> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MeDto> ReadAsync() => _completion.Task;
        public void Complete(MeDto user) => _completion.SetResult(user);
    }

    /// <summary>只分发当前测试需要的 me 契约。</summary>
    public class AuthApiProxy : DispatchProxy
    {
        public MeSource Source { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IAuthApi.GetMeAsync) ? Source.ReadAsync() : throw new NotSupportedException(targetMethod?.Name);
    }

    /// <summary>内存令牌明确暴露会话切换，不接触用户令牌缓存。</summary>
    private sealed class MemoryTokenStore : ITokenStore
    {
        private string? _token;
        public bool HasAccessToken => _token is not null;
        public string? GetAccessToken() => _token;
        public void SetAccessToken(string accessToken) => _token = accessToken;
        public void Clear() => _token = null;
    }

    /// <summary>仅清除测试会话的令牌与身份。</summary>
    private sealed class SessionStub(ITokenStore tokens, ICurrentUserService current) : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() { tokens.Clear(); current.Clear(); }
        public void EndSession(SessionEndReason reason)
        { ClearSession(); SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason)); }
    }

    /// <summary>直接运行 API 委托以控制异步响应先后。</summary>
    private sealed class RunnerStub : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>个人设置不加载管理数据，异步刷新不越过页签生命周期。</summary>
[TestClass]
public sealed class PersonalSettingsLifecycleTests
{
    /// <summary>写入中禁止关闭，失败保留输入；强制关闭后成功响应不得结束新会话。</summary>
    [TestMethod]
    public async Task PasswordWrite_BlocksClosePreservesFailureAndIgnoresLateSuccess()
    {
        var response = new TaskCompletionSource<ServiceResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = System.Reflection.DispatchProxy.Create<SwiftExport.Contracts.Apis.AuthApis.IAuthApi,
            DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)api).Handler = (_, _) => response.Task;
        var session = new TestSession();
        using var model = new SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels.ChangePasswordViewModel(api, new Runner(), session);
        var input = new SwiftExportClient.Features.SystemSettings.Passwords.Change.Models.ChangePasswordInput("old", "secret1", "secret1");
        model.ReadInput = () => input;
        var pending = model.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(model.SaveCommand.CanExecute(null));
        Assert.IsFalse(model.CloseState.CanClose(SwiftExportClient.Services.UI.DialogResult.Close));
        response.SetResult(ServiceResult<bool>.Fail("旧密码错误"));
        await pending;
        Assert.AreEqual("旧密码错误", model.StatusMessage);
        Assert.AreSame(input, model.ReadInput());
        Assert.IsTrue(model.SaveCommand.CanExecute(null));
        response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = model.SaveCommand.ExecuteAsync(null);
        model.CloseState.ForceClose();
        response.SetResult(ServiceResult<bool>.Ok(true));
        await pending;
        Assert.AreEqual(0, session.EndCount);
    }

    /// <summary>无管理权限也能进入概览，关闭后迟到刷新不回写提示。</summary>
    [TestMethod]
    public async Task PersonalOverview_DoesNotLoadManagementAndIgnoresLateRefresh()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { UserId = "personal" });
        var auth = new PendingAuth();
        using var model = new SettingsPageViewModel(
            () => throw new AssertFailedException("不应加载管理模型"),
            () => throw new AssertFailedException("不应加载字典"),
            () => throw new NotSupportedException(), () => throw new NotSupportedException(),
            user, new SettingsDialogStub(), authService: auth);
        await model.InitializeAsync();
        Assert.AreEqual(SettingsPage.Personal, model.CurrentPage);
        Assert.IsFalse(model.CanRead(SettingsPage.Users));
        var pending = model.RefreshPermissionsCommand.ExecuteAsync(null);
        Assert.IsFalse(model.RefreshPermissionsCommand.CanExecute(null));
        model.Dispose();
        auth.Response.SetResult(ServiceResult<MeDto>.Ok(new MeDto()));
        await pending;
        Assert.AreEqual(string.Empty, model.RefreshStatus);
    }

    /// <summary>刷新失败不改变现有权限；下一次请求仍可执行。</summary>
    [TestMethod]
    public async Task RefreshFailure_KeepsSnapshotAndAllowsRetry()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { UserId = "personal", Permissions = ["System.Users.View"] });
        var auth = new PendingAuth();
        using var model = new SettingsPageViewModel(
            () => throw new AssertFailedException(), () => throw new AssertFailedException(),
            () => throw new NotSupportedException(), () => throw new NotSupportedException(),
            user, new SettingsDialogStub(), authService: auth);
        var pending = model.RefreshPermissionsCommand.ExecuteAsync(null);
        auth.Response.SetResult(ServiceResult<MeDto>.Fail("离线"));
        await pending;
        Assert.AreEqual("离线", model.RefreshStatus);
        Assert.IsTrue(model.CanRead(SettingsPage.Users));
        Assert.IsTrue(model.RefreshPermissionsCommand.CanExecute(null));
    }

    /// <summary>控制权限读取完成时刻。</summary>
    private sealed class PendingAuth : IAuthService
    {
        public TaskCompletionSource<ServiceResult<MeDto>> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync() => Response.Task;
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password) => throw new NotSupportedException();
        public Task<bool> TryRestoreSessionAsync() => throw new NotSupportedException();
    }

    /// <summary>直接执行测试 API 委托。</summary>
    private sealed class Runner : SwiftExportClient.Services.Api.IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>记录旧密码响应是否错误结束会话。</summary>
    private sealed class TestSession : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public int EndCount { get; private set; }
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason)
        {
            EndCount++;
            SessionEnded?.Invoke(this, new(reason));
        }
    }
}

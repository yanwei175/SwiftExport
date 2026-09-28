using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>验证设置中心权限与账号密码写命令。</summary>
[TestClass]
public sealed class SettingsViewModelTests
{
    /// <summary>验证匿名会话不能直接调用本人改密窗口服务。</summary>
    [TestMethod]
    public void SettingsWindowService_ShouldRejectPersonalWindowWithoutAuthenticatedSession()
    {
        var messages = new FakeMessageService();
        var session = new FakeSessionService();
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var service = new SettingsWindowService(
            provider,
            new CurrentUserService(),
            session,
            messages);

        service.ShowChangePassword();

        StringAssert.Contains(messages.LastMessage, "请先登录");
    }

    /// <summary>验证角色名称不会替代管理权限，个人入口仍保留。</summary>
    [TestMethod]
    public void SettingsCenter_ShouldKeepPersonalEntryAndHideManagementWithoutPermission()
    {
        var currentUser = CreateCurrentUser(roles: ["Admin"]);
        var windows = new FakeSettingsWindowService();
        var navigation = new SettingsNavigationSpy();
        var viewModel = new SettingsCenterViewModel(currentUser, windows, navigation);

        viewModel.ChangePasswordCommand.Execute(null);

        Assert.IsFalse(viewModel.CanManageSettings);
        Assert.IsFalse(viewModel.OpenSystemSettingsCommand.CanExecute(null));
        Assert.AreEqual(1, windows.ChangePasswordCount);
    }

    /// <summary>验证管理入口只由稳定权限快照启用。</summary>
    [TestMethod]
    public void SettingsCenter_ShouldEnableManagementFromPermissionSnapshot()
    {
        var currentUser = CreateCurrentUser(["System.Users.View", "System.Roles.View", "System.Dictionary.View"]);
        var windows = new FakeSettingsWindowService();
        var navigation = new SettingsNavigationSpy();
        var viewModel = new SettingsCenterViewModel(currentUser, windows, navigation);

        viewModel.OpenSystemSettingsCommand.Execute(null);

        Assert.IsTrue(viewModel.CanManageSettings);
        Assert.AreEqual("system-settings", navigation.LastRoute);
    }

    /// <summary>验证创建用户防重复提交并提示后续模块分配。</summary>
    [TestMethod]
    public async Task RegisterUser_ShouldRejectDuplicateSubmissionAndExplainEmptyPermissions()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi
        {
            RegisterHandler = async request =>
            {
                await gate.Task;
                return ServiceResult<Guid>.Ok(Guid.NewGuid());
            }
        };
        var messages = new FakeMessageService();
        var viewModel = new UserRegistrationViewModel(api, new PassThroughApiCallRunner(), messages)
        {
            UserName = "new-user",
            NickName = "新用户"
        };
        var closeCount = 0;
        viewModel.RequestClose += (_, _) => closeCount++;
        var passwords = new UserRegistrationPasswordInput("secret1", "secret1");

        var first = viewModel.SubmitCommand.ExecuteAsync(passwords);
        var second = viewModel.SubmitCommand.ExecuteAsync(passwords);
        await Task.Yield();

        Assert.AreEqual(1, api.RegisterCallCount);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual(1, closeCount);
        StringAssert.Contains(messages.LastMessage, "默认没有角色与业务权限");
        Assert.AreEqual(nameof(UserRegistrationPasswordInput), passwords.ToString());
    }

    /// <summary>验证本人改密失败不退出、成功后结束旧凭据会话。</summary>
    [TestMethod]
    public async Task ChangePassword_ShouldPreserveFailureAndEndSessionAfterSuccess()
    {
        var api = new FakeAuthApi
        {
            ChangePasswordResult = ServiceResult<bool>.Fail("当前密码错误")
        };
        var session = new FakeSessionService();
        var viewModel = new ChangePasswordViewModel(api, new PassThroughApiCallRunner(), session);
        var closeCount = 0;
        viewModel.RequestClose += (_, _) => closeCount++;
        var input = new ChangePasswordInput("old-password", "new-password", "new-password");

        await viewModel.SubmitCommand.ExecuteAsync(input);

        Assert.AreEqual(0, closeCount);
        Assert.IsTrue(viewModel.IsError);
        StringAssert.Contains(viewModel.StatusMessage, "当前密码错误");

        api.ChangePasswordResult = ServiceResult<bool>.Ok(true);
        await viewModel.SubmitCommand.ExecuteAsync(input);

        Assert.AreEqual(1, closeCount);
        Assert.AreEqual(SessionEndReason.CredentialsChanged, session.LastReason);
        Assert.AreEqual("old-password", api.LastChangePasswordRequest?.CurrentPassword);
        Assert.AreEqual(nameof(ChangePasswordInput), input.ToString());
    }

    /// <summary>验证管理员目标可见但不能从普通重置入口提交。</summary>
    [TestMethod]
    public async Task ResetPassword_ShouldKeepAdminVisibleButRejectAdminTarget()
    {
        var normalUser = User("normal", isAdmin: false);
        var adminUser = User("admin", isAdmin: true);
        var api = new FakeAuthApi
        {
            UsersResult = ServiceResult<List<UserSummaryDto>>.Ok([adminUser, normalUser])
        };
        var viewModel = new ResetPasswordViewModel(
            api,
            new PassThroughApiCallRunner(),
            new FakeMessageService());
        await viewModel.InitializeAsync();

        Assert.AreEqual(2, viewModel.Users.Count);
        Assert.AreSame(normalUser, viewModel.SelectedUser);

        viewModel.SelectedUser = adminUser;
        await viewModel.SubmitCommand.ExecuteAsync(new ResetPasswordInput("secret1", "secret1"));

        Assert.AreEqual(0, api.ResetPasswordCallCount);
        StringAssert.Contains(viewModel.StatusMessage, "管理员");
        viewModel.Dispose();
    }

    /// <summary>会话切换释放表单后，创建与重置的迟到成功均不得发出关闭事件或成功提示。</summary>
    [TestMethod]
    public async Task AccountForms_DisposedDuringWriteIgnoreLateSuccess()
    {
        var registrationResponse = new TaskCompletionSource<ServiceResult<Guid>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resetResponse = new TaskCompletionSource<ServiceResult<bool>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi
        {
            RegisterHandler = _ => registrationResponse.Task,
            ResetPasswordHandler = _ => resetResponse.Task
        };
        var messages = new FakeMessageService();
        var registration = new UserRegistrationViewModel(api, new PassThroughApiCallRunner(), messages)
        { UserName = "new-user" };
        var reset = new ResetPasswordViewModel(api, new PassThroughApiCallRunner(), messages)
        { SelectedUser = User("normal", isAdmin: false) };
        var closeCount = 0;
        registration.RequestClose += (_, _) => closeCount++;
        reset.RequestClose += (_, _) => closeCount++;

        var registrationWrite = registration.SubmitCommand.ExecuteAsync(
            new UserRegistrationPasswordInput("secret1", "secret1"));
        var resetWrite = reset.SubmitCommand.ExecuteAsync(new ResetPasswordInput("secret2", "secret2"));
        Assert.AreEqual(1, api.RegisterCallCount);
        Assert.AreEqual(1, api.ResetPasswordCallCount);
        registration.Dispose();
        reset.Dispose();
        registrationResponse.SetResult(ServiceResult<Guid>.Ok(Guid.NewGuid()));
        resetResponse.SetResult(ServiceResult<bool>.Ok(true));
        await Task.WhenAll(registrationWrite, resetWrite);

        Assert.AreEqual(0, closeCount);
        Assert.AreEqual(string.Empty, messages.LastMessage);
    }

    private static CurrentUserService CreateCurrentUser(
        IReadOnlyList<string>? permissions = null,
        IReadOnlyList<string>? roles = null)
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(new MeDto
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = "tester",
            Permissions = permissions?.ToList() ?? [],
            Roles = roles?.ToList() ?? []
        });
        return service;
    }

    private static UserSummaryDto User(string name, bool isAdmin)
        => new() { Uid = Guid.NewGuid(), UserName = name, NickName = name, IsAdmin = isAdmin };

    private sealed class FakeSettingsWindowService : ISettingsWindowService
    {
        public int ChangePasswordCount { get; private set; }
        public int SystemSettingsCount { get; private set; }
        public void ShowChangePassword() => ChangePasswordCount++;
        public void ShowSystemSettings() => SystemSettingsCount++;
    }

    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<RegisterRequestDto, Task<ServiceResult<Guid>>>? RegisterHandler { get; init; }
        public Func<AdminResetPasswordRequestDto, Task<ServiceResult<bool>>>? ResetPasswordHandler { get; init; }
        public ServiceResult<bool> ChangePasswordResult { get; set; } = ServiceResult<bool>.Ok(true);
        public ServiceResult<List<UserSummaryDto>> UsersResult { get; init; } = ServiceResult<List<UserSummaryDto>>.Ok([]);
        public int RegisterCallCount { get; private set; }
        public int ResetPasswordCallCount { get; private set; }
        public ChangePasswordRequestDto? LastChangePasswordRequest { get; private set; }

        public Task<LoginTokenDto> LoginAsync(LoginRequestDto request) => throw new NotSupportedException();
        public Task<MeDto> GetMeAsync() => throw new NotSupportedException();

        public Task<ServiceResult<Guid>> RegisterAsync(RegisterRequestDto request)
        {
            RegisterCallCount++;
            return RegisterHandler?.Invoke(request)
                ?? Task.FromResult(ServiceResult<Guid>.Ok(Guid.NewGuid()));
        }

        public Task<ServiceResult<bool>> ChangePasswordAsync(ChangePasswordRequestDto request)
        {
            LastChangePasswordRequest = request;
            return Task.FromResult(ChangePasswordResult);
        }

        public Task<ServiceResult<bool>> ResetUserPasswordAsync(AdminResetPasswordRequestDto request)
        {
            ResetPasswordCallCount++;
            return ResetPasswordHandler?.Invoke(request)
                ?? Task.FromResult(ServiceResult<bool>.Ok(true));
        }

        public Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync()
            => Task.FromResult(UsersResult);

    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败")
            => action();

        public async Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败")
        {
            try
            {
                return await action();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ServiceResult<T>.Fail(string.Empty);
            }
        }

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class FakeMessageService : IMessageService
    {
        public string LastMessage { get; private set; } = string.Empty;
        public void ShowInfo(string title, string message) => LastMessage = message;
        public void ShowSuccess(string title, string message) => LastMessage = message;
        public void ShowWarning(string title, string message) => LastMessage = message;
        public void ShowError(string title, string message) => LastMessage = message;
    }

    private sealed class FakeSessionService : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public SessionEndReason? LastReason { get; private set; }
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason)
        {
            LastReason = reason;
            SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmationResult { get; init; } = true;

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
            => Task.FromResult(ConfirmationResult);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

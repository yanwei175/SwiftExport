using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserPermissions.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>验证设置中心权限、账号密码写命令和模块整组保存行为。</summary>
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
        var viewModel = new SettingsCenterViewModel(currentUser, windows);

        viewModel.ChangePasswordCommand.Execute(null);

        Assert.IsFalse(viewModel.CanManageSettings);
        Assert.IsFalse(viewModel.RegisterUserCommand.CanExecute(null));
        Assert.AreEqual(1, windows.ChangePasswordCount);
    }

    /// <summary>验证管理入口只由稳定权限快照启用。</summary>
    [TestMethod]
    public void SettingsCenter_ShouldEnableManagementFromPermissionSnapshot()
    {
        var currentUser = CreateCurrentUser([PermissionKeys.SystemSettingsManage]);
        var windows = new FakeSettingsWindowService();
        var viewModel = new SettingsCenterViewModel(currentUser, windows);

        viewModel.RegisterUserCommand.Execute(null);
        viewModel.ResetPasswordCommand.Execute(null);
        viewModel.ManageUserModulesCommand.Execute(null);
        viewModel.ManageDictionariesCommand.Execute(null);

        Assert.IsTrue(viewModel.CanManageSettings);
        Assert.AreEqual(1, windows.RegisterCount);
        Assert.AreEqual(1, windows.ResetCount);
        Assert.AreEqual(1, windows.ModulesCount);
        Assert.AreEqual(1, windows.DictionariesCount);
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
        StringAssert.Contains(messages.LastMessage, "默认没有模块权限");
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

    /// <summary>验证新用户空模块状态和完整模块集合保存。</summary>
    [TestMethod]
    public async Task UserModules_ShouldLoadEmptyUserAndSaveCompleteModuleSet()
    {
        var user = User("new-user", isAdmin: false);
        var api = new FakeAuthApi
        {
            UsersResult = ServiceResult<List<UserSummaryDto>>.Ok([user]),
            GetModulesHandler = _ => ServiceResult<UserModulePermissionsDto>.Ok(new UserModulePermissionsDto
            {
                UserId = user.Uid,
                ModulePermissions = []
            })
        };
        var viewModel = new UserModulePermissionsViewModel(
            api,
            new PassThroughApiCallRunner(),
            new FakeDialogService());

        await viewModel.InitializeAsync();

        StringAssert.Contains(viewModel.StatusMessage, "没有模块权限");
        viewModel.Modules.Single(module => module.PermissionKey == PermissionKeys.ModuleSalesAccess).IsSelected = true;
        viewModel.Modules.Single(module => module.PermissionKey == PermissionKeys.ModuleShippingAccess).IsSelected = true;
        await viewModel.SaveCommand.ExecuteAsync(null);

        CollectionAssert.AreEquivalent(
            new[] { PermissionKeys.ModuleSalesAccess, PermissionKeys.ModuleShippingAccess },
            api.LastSavedModules!.ToArray());
        Assert.IsFalse(viewModel.IsDirty);
        StringAssert.Contains(viewModel.StatusMessage, "下次登录");
        viewModel.Dispose();
    }

    /// <summary>验证取消放弃变更后保持原用户与原勾选。</summary>
    [TestMethod]
    public async Task UserModules_ShouldKeepCurrentUserWhenDiscardConfirmationIsCancelled()
    {
        var firstUser = User("first", isAdmin: false);
        var secondUser = User("second", isAdmin: false);
        var api = new FakeAuthApi
        {
            UsersResult = ServiceResult<List<UserSummaryDto>>.Ok([firstUser, secondUser]),
            GetModulesHandler = userId => ServiceResult<UserModulePermissionsDto>.Ok(new UserModulePermissionsDto
            {
                UserId = userId,
                ModulePermissions = []
            })
        };
        var dialogs = new FakeDialogService { ConfirmationResult = false };
        var viewModel = new UserModulePermissionsViewModel(api, new PassThroughApiCallRunner(), dialogs);
        await viewModel.InitializeAsync();
        viewModel.Modules[0].IsSelected = true;

        await viewModel.SelectUserCommand.ExecuteAsync(secondUser);

        Assert.AreSame(firstUser, viewModel.SelectedUser);
        Assert.IsTrue(viewModel.Modules[0].IsSelected);
        viewModel.Dispose();
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
        public int RegisterCount { get; private set; }
        public int ResetCount { get; private set; }
        public int ModulesCount { get; private set; }
        public int DictionariesCount { get; private set; }
        public void ShowChangePassword() => ChangePasswordCount++;
        public void ShowUserRegistration() => RegisterCount++;
        public void ShowResetPassword() => ResetCount++;
        public void ShowUserModules() => ModulesCount++;
        public void ShowDictionaries() => DictionariesCount++;
    }

    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<RegisterRequestDto, Task<ServiceResult<Guid>>>? RegisterHandler { get; init; }
        public ServiceResult<bool> ChangePasswordResult { get; set; } = ServiceResult<bool>.Ok(true);
        public ServiceResult<List<UserSummaryDto>> UsersResult { get; init; } = ServiceResult<List<UserSummaryDto>>.Ok([]);
        public Func<Guid, ServiceResult<UserModulePermissionsDto>>? GetModulesHandler { get; init; }
        public int RegisterCallCount { get; private set; }
        public int ResetPasswordCallCount { get; private set; }
        public ChangePasswordRequestDto? LastChangePasswordRequest { get; private set; }
        public List<string>? LastSavedModules { get; private set; }

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
            return Task.FromResult(ServiceResult<bool>.Ok(true));
        }

        public Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync()
            => Task.FromResult(UsersResult);

        public Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(Guid userId)
            => Task.FromResult(GetModulesHandler?.Invoke(userId)
                ?? ServiceResult<UserModulePermissionsDto>.Fail("未配置测试结果"));

        public Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(
            Guid userId,
            SaveUserModulePermissionsRequestDto request)
        {
            LastSavedModules = request.ModulePermissions.ToList();
            return Task.FromResult(ServiceResult<UserModulePermissionsDto>.Ok(new UserModulePermissionsDto
            {
                UserId = userId,
                ModulePermissions = request.ModulePermissions.ToList()
            }));
        }
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

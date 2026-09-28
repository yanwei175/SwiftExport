using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Forms.Views;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.Services;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using Wpf.Ui;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>在 STA 与真实控件资源下验证设置页面导航和模块 Dialog 边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowInteractionTests
{
    /// <summary>个人操作仍有中文无障碍名称和键盘焦点顺序。</summary>
    [TestMethod]
    public void SettingsPage_ExposesNamedPersonalActionsInKeyboardOrder()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var user = CreateUser(["System.Users.View", "System.Roles.View"]);
            using var model = new SettingsPageViewModel(
                () => throw new AssertFailedException(), () => throw new AssertFailedException(),
                () => throw new AssertFailedException(), () => throw new AssertFailedException(),
                user, new SettingsDialogStub());
            var view = new SystemSettingsView { DataContext = model };
            var window = new Window { Width = 720, Height = 640, ShowInTaskbar = false,
                WindowStyle = WindowStyle.None, Content = view };
            try
            {
                window.Show();
                await DrainAsync(window.Dispatcher);
                Assert.AreEqual(SettingsPage.Personal, model.CurrentPage);
                var buttons = new[]
                {
                    (ButtonBase)view.FindName("ChangePasswordNavigation"),
                    (ButtonBase)view.FindName("RefreshPermissionsNavigation")
                };
                CollectionAssert.AreEqual(new[] { "修改我的密码", "刷新我的权限" },
                    buttons.Select(AutomationProperties.GetName).ToArray());
                Assert.IsTrue(buttons.All(button => button.Focusable && KeyboardNavigation.GetIsTabStop(button)));
                Assert.IsTrue(buttons[0].Focus());
                Assert.IsTrue(buttons[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.AreSame(buttons[1], Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>创建表单经公共 ShowAsync 的主按钮保存；失败保留密码，成功返回目标。</summary>
    [TestMethod]
    public void RegistrationDialog_PublicPrimaryButtonPreservesFailureAndReturnsSuccess()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var user = CreateUser(["System.Users.View"]);
            var createdId = Guid.NewGuid();
            var reject = true;
            RegisterRequestDto? submitted = null;
            var api = CreateAuthApi((method, args) => method.Name switch
            {
                nameof(IAuthApi.RegisterAsync) => Register((RegisterRequestDto)args![0]!),
                _ => throw new NotSupportedException(method.Name)
            });
            Task<ServiceResult<Guid>> Register(RegisterRequestDto request)
            {
                submitted = request;
                return Task.FromResult(reject ? ServiceResult<Guid>.Fail("用户名冲突")
                    : ServiceResult<Guid>.Ok(createdId));
            }
            var dialogs = new ScriptedDialogService();
            dialogs.Handler = async (content, options, state) =>
            {
                Assert.IsInstanceOfType<SettingsFormContent>(content);
                var form = ((SettingsFormContent)content).Body as UserRegistrationView;
                Assert.IsNotNull(form);
                Assert.AreEqual("创建用户", options.Title);
                Assert.AreEqual("创建用户", options.PrimaryButtonText);
                Assert.IsNotNull(options.PrimaryButtonCommand);
                ((TextBox)form.FindName("UserNameBox")).Text = "new-user";
                ((UserRegistrationViewModel)form.DataContext).UserName = "new-user";
                ((PasswordBox)form.FindName("PasswordInput")).Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                await options.PrimaryButtonCommand.ExecuteAsync(null);
                Assert.AreEqual("用户名冲突", ((UserRegistrationViewModel)form.DataContext).StatusMessage);
                Assert.AreEqual("secret1", ((PasswordBox)form.FindName("PasswordInput")).Password);
                Assert.IsFalse(state!.CanClose(DialogResult.Close));
                reject = false;
                await options.PrimaryButtonCommand.ExecuteAsync(null);
                Assert.AreEqual("secret1", submitted?.Password);
                return DialogResult.Primary;
            };
            using var service = CreateRegistrationService(dialogs, user, api);
            var result = await service.ShowCreateUserAsync();
            Assert.IsTrue(result.Saved);
            Assert.AreEqual(createdId, result.TargetId);
            Assert.AreEqual("new-user", result.CreatedUserName);
            Assert.AreEqual(1, dialogs.ShowCalls);
            Assert.IsFalse(service.HasActiveForm);
        });
    }

    /// <summary>放弃确认留在当前内容内；强制关闭立刻擦除密码并忽略取消返回。</summary>
    [TestMethod]
    public void RegistrationDialog_RejectDiscardThenForceCloseClearsPassword()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var user = CreateUser(["System.Users.View"]);
            var dialogs = new ScriptedDialogService();
            var response = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            SettingsFormContent? content = null;
            IDialogContentState? state = null;
            dialogs.Handler = (body, _, current) =>
            { content = (SettingsFormContent)body; state = current; return response.Task; };
            using var service = CreateRegistrationService(dialogs, user, CreateAuthApi((method, _) =>
                throw new NotSupportedException(method.Name)));
            var pending = service.ShowCreateUserAsync();
            Assert.IsTrue(service.HasActiveForm);
            var form = content?.Body as UserRegistrationView;
            Assert.IsNotNull(form);
            var password = (PasswordBox)form.FindName("PasswordInput");
            password.Password = "sensitive";
            Assert.IsFalse(state!.CanClose(DialogResult.Close));
            var closeState = ((UserRegistrationViewModel)form.DataContext).CloseState;
            Assert.IsTrue(closeState.IsDiscardPromptVisible);
            closeState.KeepEditingCommand.Execute(null);
            Assert.IsFalse(closeState.IsDiscardPromptVisible);
            Assert.AreEqual("sensitive", password.Password);
            service.ForceClose();
            Assert.AreEqual(string.Empty, password.Password);
            response.SetResult(DialogResult.Close);
            Assert.IsFalse((await pending).Saved);
            Assert.IsFalse(service.HasActiveForm);
        });
    }

    /// <summary>初始化期间撤销角色读取权后不得迟到展示授权表单。</summary>
    [TestMethod]
    public void UserRolesDialog_RevokedDuringInitializationNeverOpens()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var user = CreateUser(["System.Users.View", "System.Roles.View"]);
            var source = new TaskCompletionSource<ServiceResult<UserRolesDto>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var target = new ManagedUserDto { Uid = Guid.NewGuid(), UserName = "target", Version = 1 };
            var api = DispatchProxy.Create<ISystemRbacApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)api).Handler = (method, _) => method.Name switch
            {
                nameof(ISystemRbacApi.GetUserRolesAsync) => source.Task,
                _ => throw new NotSupportedException(method.Name)
            };
            var dialogs = new ScriptedDialogService();
            using var service = new SettingsDialogService(dialogs, user,
                () => throw new AssertFailedException(), () => throw new AssertFailedException(),
                () => throw new AssertFailedException(), () => throw new AssertFailedException(),
                () => new UserRolesFormViewModel(api, new PassThroughRunner(), new RefreshingAuth()),
                () => throw new AssertFailedException(), () => throw new AssertFailedException());
            var pending = service.ShowUserRolesAsync(target);
            Assert.IsTrue(service.HasActiveForm);
            user.SetCurrentUser(new MeDto { UserId = user.CurrentUser!.UserId, IsAdmin = true,
                Permissions = ["System.Users.View"] });
            source.SetResult(ServiceResult<UserRolesDto>.Ok(new UserRolesDto
                { UserId = target.Uid, Version = 1 }));
            Assert.IsFalse((await pending).Saved);
            Assert.AreEqual(0, dialogs.ShowCalls);
            Assert.IsFalse(service.HasActiveForm);
        });
    }

    /// <summary>公共目录稳定键仍只读，设置重构不引入动态权限编辑。</summary>
    [TestMethod]
    public void PermissionCatalog_StableKeysRemainReadOnly()
    {
        string[] keys = [PermissionKeys.SalesOrderView, PermissionKeys.SalesOrderMaintain,
            PermissionKeys.SalesOrderDelete, PermissionKeys.SalesOrderConfirm,
            PermissionKeys.SalesOrderCancelConfirmation, PermissionKeys.SalesOrderLifecycle];
        foreach (var key in keys) Assert.AreEqual("Sales", PermissionCatalog.Find(key)?.GroupKey);
        Assert.IsFalse(PermissionCatalog.All.Any(item => item.GroupKey == "Orders"));
        Assert.AreEqual(PermissionCatalog.All.Count,
            PermissionCatalog.All.Select(item => item.PermissionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(((ICollection<PermissionDefinition>)PermissionCatalog.All).IsReadOnly);
    }

    private static CurrentUserService CreateUser(IReadOnlyList<string> permissions)
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), UserName = "tester",
            IsAdmin = true, Permissions = permissions.ToList() });
        return user;
    }

    private static SettingsDialogService CreateRegistrationService(IDialogService dialogs,
        ICurrentUserService user, IAuthApi api) => new(dialogs, user,
        () => throw new AssertFailedException(),
        () => new UserRegistrationViewModel(api, new PassThroughRunner(), new WindowMessageService()),
        () => throw new AssertFailedException(), () => throw new AssertFailedException(),
        () => throw new AssertFailedException(), () => throw new AssertFailedException(),
        () => throw new AssertFailedException());

    private static IAuthApi CreateAuthApi(Func<MethodInfo, object?[]?, object> handler)
    {
        var api = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)api).Handler = handler;
        return api;
    }

    private static Task DrainAsync(Dispatcher dispatcher)
        => dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private sealed class ScriptedDialogService : IDialogService
    {
        public Func<object, DialogOptions, IDialogContentState?, Task<DialogResult>>? Handler { get; set; }
        public int ShowCalls { get; private set; }
        public Task<DialogResult> ShowAsync(object content, DialogOptions options,
            IDialogContentState? state = null, CancellationToken cancellationToken = default)
        {
            ShowCalls++;
            return Handler?.Invoke(content, options, state) ?? Task.FromResult(DialogResult.Close);
        }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定",
            string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PassThroughRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action,
            string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken, string failureMessage = "请求失败")
        {
            try { return await action(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return ServiceResult<T>.Fail(string.Empty); }
        }
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action,
            string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action());
    }

    private sealed class RefreshingAuth : IAuthService
    {
        public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync()
            => Task.FromResult(ServiceResult<MeDto>.Ok(new MeDto()));
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password)
            => throw new NotSupportedException();
        public Task<bool> TryRestoreSessionAsync() => throw new NotSupportedException();
    }
}

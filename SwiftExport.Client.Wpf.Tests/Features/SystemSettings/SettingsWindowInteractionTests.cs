using Microsoft.VisualStudio.TestTools.UnitTesting;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Features.SystemSettings.Workspace.Views;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>在真实 WPF STA 和应用资源下验证设置中心的键盘焦点与无障碍名称。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowInteractionTests
{
    /// <summary>验证个人刷新与管理入口具备中文名称并能按 Tab 顺序移动焦点。</summary>
    [TestMethod]
    public void SettingsCenter_ShouldExposeNamedActionsInKeyboardTabOrder()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto
            {
                UserId = Guid.NewGuid().ToString(),
                UserName = "admin",
                Permissions = ["System.Users.View", "System.Roles.View", "System.Dictionary.View"]
            });
            var view = new SettingsCenterView
            {
                DataContext = new SettingsCenterViewModel(currentUser, new FakeSettingsWindowService())
            };
            var window = new Window
            {
                Width = 720,
                Height = 640,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view
            };

            try
            {
                window.Show();
                await DrainDispatcherAsync(window.Dispatcher);
                var buttons = FindVisualDescendants<ButtonBase>(view).ToList();
                Assert.AreEqual(3, buttons.Count);
                CollectionAssert.AreEqual(
                    new[] { "修改我的密码", "刷新我的权限", "打开系统设置" },
                    buttons.Select(AutomationProperties.GetName).ToArray());
                Assert.IsTrue(buttons.All(button => button.Focusable && KeyboardNavigation.GetIsTabStop(button)));

                Assert.IsTrue(buttons[0].Focus());
                Assert.IsTrue(buttons[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.AreSame(buttons[1], Keyboard.FocusedElement);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>验证改密窗口初始焦点、取消返回值和关闭时密码清理。</summary>
    [TestMethod]
    public void ChangePasswordWindow_ShouldFocusCurrentPasswordAndClearOnCancel()
    {
        DatePickerAssistTests.RunWithApplication(() =>
        {
            ApplicationResourceLoader.Load();
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            {
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            var dialog = new ChangePasswordWindow(new ChangePasswordViewModel(
                CreateThrowingAuthApi(),
                new UnusedApiCallRunner(),
                new FakeSessionService()));
            var passwordBoxCount = 0;
            var currentPasswordHadFocus = false;
            var cancelIsConfigured = false;

            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var passwordBoxes = FindVisualDescendants<global::Wpf.Ui.Controls.PasswordBox>(dialog).ToList();
                    passwordBoxCount = passwordBoxes.Count;
                    currentPasswordHadFocus = passwordBoxes.FirstOrDefault() is { } current
                        && ReferenceEquals(current, Keyboard.FocusedElement);
                    foreach (var passwordBox in passwordBoxes)
                    {
                        passwordBox.Password = "sensitive";
                    }

                    var cancel = FindVisualDescendants<global::Wpf.Ui.Controls.Button>(dialog)
                        .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "取消", StringComparison.Ordinal));
                    cancelIsConfigured = cancel?.IsCancel == true;
                    dialog.DialogResult = false;
                }));

                var result = dialog.ShowDialog();

                Assert.AreEqual(3, passwordBoxCount);
                Assert.IsTrue(currentPasswordHadFocus);
                Assert.IsTrue(cancelIsConfigured);
                Assert.AreEqual(false, result);
                Assert.IsTrue(FindVisualDescendants<global::Wpf.Ui.Controls.PasswordBox>(dialog)
                    .All(passwordBox => string.IsNullOrEmpty(passwordBox.Password)));
            }
            finally
            {
                if (dialog.IsVisible)
                {
                    dialog.ForceClose();
                }

                owner.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>独立系统窗口在 STA 中按用户读取权加载，并完成普通关闭。</summary>
    [TestMethod]
    public void SystemSettingsWindow_ShouldLoadAndCloseWithoutReentrancy()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(),
                Permissions = ["System.Users.View"] });
            var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Users", dialog.CurrentPageName);
                Assert.AreSame(owner, dialog.Owner);
                Assert.IsNotNull(FindVisualDescendants<UserManagementView>(dialog).SingleOrDefault());
                dialog.Close();
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.IsFalse(dialog.IsVisible);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>在真实 WPF 视觉树中完成用户与角色授权，并切到只读目录页。</summary>
    [TestMethod]
    public void SystemSettingsWindow_CheckboxesAndButtonsBindToVersionedWrites()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
            var api = DispatchProxy.Create<ISystemRbacApi,
                RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
            ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new RefreshingAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 900, Height = 760, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Users", dialog.CurrentPageName);
                Assert.HasCount(1, model.Users);

                var userAssign = FindVisualDescendants<ButtonBase>(dialog)
                    .Single(item => item.Content?.ToString() == "分配角色");
                userAssign.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<UserRolesFormView>(dialog).Any(), "用户角色弹窗");
                var userForm = FindVisualDescendants<UserRolesFormView>(dialog).Single();
                var userRole = FindVisualDescendants<CheckBox>(userForm)
                    .Single(item => item.Content?.ToString()?.Contains("VIEWER", StringComparison.Ordinal) == true);
                userRole.IsChecked = true;
                Assert.IsTrue(model.UserRoleChoices.Single().IsSelected);
                var saveUser = FindVisualDescendants<ButtonBase>(userForm)
                    .Single(item => item.Content?.ToString() == "保存角色");
                saveUser.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => state.UserRoles.SequenceEqual(new[] { state.Role.Uid })
                        && !FindVisualDescendants<UserRolesFormView>(dialog).Any(), "用户角色保存并关闭");
                CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);

                ((ButtonBase)dialog.FindName("RolesNavigation")).RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Roles", dialog.CurrentPageName);
                Assert.HasCount(2, model.Permissions);
                var assignButton = FindVisualDescendants<ButtonBase>(dialog)
                    .Single(item => item.Content?.ToString() == "分配权限"
                        && item is FrameworkElement element
                        && element.DataContext is ManagedRoleDto role && !role.IsSystem);
                assignButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<RolePermissionsFormView>(dialog).Any(), "角色权限弹窗");
                var roleForm = FindVisualDescendants<RolePermissionsFormView>(dialog).Single();
                var assignable = FindVisualDescendants<CheckBox>(roleForm)
                    .Single(item => item.Content?.ToString()?.Contains("供应商查看", StringComparison.Ordinal) == true);
                var reserved = FindVisualDescendants<CheckBox>(roleForm)
                    .Single(item => item.Content?.ToString()?.Contains("用户管理", StringComparison.Ordinal) == true);
                Assert.IsFalse(reserved.IsEnabled);
                assignable.IsChecked = true;
                Assert.IsTrue(model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected);
                ((TextBox)roleForm.FindName("PermissionSearch")).Text = "用户管理";
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.IsFalse(assignable.IsVisible);
                var saveRole = FindVisualDescendants<ButtonBase>(roleForm)
                    .Single(item => item.Content?.ToString() == "保存完整权限集合");
                saveRole.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => state.RolePermissions.SequenceEqual(new[] { state.Assignable.Uid })
                        && !FindVisualDescendants<RolePermissionsFormView>(dialog).Any(),
                    "角色权限保存并关闭");
                CollectionAssert.AreEqual(new[] { state.Assignable.Uid }, state.RolePermissions);

                ((ButtonBase)dialog.FindName("PermissionsNavigation")).RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Permissions", dialog.CurrentPageName);
                var catalog = FindVisualDescendants<DataGrid>(dialog).Single();
                Assert.IsTrue(catalog.IsReadOnly);
                Assert.AreEqual(2, catalog.Items.Count);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>行内编辑分别提交用户与角色资料，保存成功后关闭各自的本地表单。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RowProfileFormsSaveSelectedTargets()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
            var api = DispatchProxy.Create<ISystemRbacApi,
                RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
            ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View"] });
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new RefreshingAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 900, Height = 760, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show(); dialog.Owner = owner; dialog.Show();
                await WaitForPageAsync(dialog, "Users", () => model.Users.Count == 1
                    && FindVisualDescendants<UserManagementView>(dialog).Any());
                var userGrid = (DataGrid)FindVisualDescendants<UserManagementView>(dialog)
                    .Single().FindName("UserGrid");
                // 选中行只更新视觉选择，读取聚合由行操作发起，避免抢先 Busy 吞掉首次点击。
                var otherUser = new ManagedUserDto { Uid = Guid.NewGuid(), UserName = "other" };
                model.Users.Add(otherUser);
                userGrid.SelectedItem = otherUser;
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual(state.User.Uid, model.SelectedUser?.Uid);
                model.Users.Remove(otherUser);
                userGrid.ScrollIntoView(state.User);
                userGrid.UpdateLayout();
                await WaitForStateAsync(dialog.Dispatcher, () => FindVisualDescendants<ButtonBase>(dialog)
                    .Any(button => button.Content?.ToString() == "编辑"
                        && button is FrameworkElement element && element.DataContext is ManagedUserDto),
                    "用户行按钮");
                FindVisualDescendants<ButtonBase>(dialog).Single(button =>
                    button.Content?.ToString() == "编辑" && button is FrameworkElement element
                    && element.DataContext is ManagedUserDto)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<UserProfileFormView>(dialog).Any(), "用户资料表单");
                var userForm = FindVisualDescendants<UserProfileFormView>(dialog).Single();
                FindVisualDescendants<TextBox>(userForm).Single(box => box.Text == "目标").Text = "目标更新";
                ((ButtonBase)userForm.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => state.User.NickName == "目标更新"
                        && !FindVisualDescendants<UserProfileFormView>(dialog).Any(), "用户资料保存");

                ((ButtonBase)dialog.FindName("RolesNavigation"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForPageAsync(dialog, "Roles", () => model.Roles.Count == 1
                    && FindVisualDescendants<RoleManagementView>(dialog).Any());
                var roleGrid = (DataGrid)FindVisualDescendants<RoleManagementView>(dialog)
                    .Single().FindName("RoleGrid");
                roleGrid.ScrollIntoView(state.Role);
                roleGrid.UpdateLayout();
                await WaitForStateAsync(dialog.Dispatcher, () => FindVisualDescendants<ButtonBase>(dialog)
                    .Any(button => button.Content?.ToString() == "编辑"
                        && button is FrameworkElement element && element.DataContext is ManagedRoleDto),
                    "角色行按钮");
                FindVisualDescendants<ButtonBase>(dialog).Single(button =>
                    button.Content?.ToString() == "编辑" && button is FrameworkElement element
                    && element.DataContext is ManagedRoleDto)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<RoleProfileFormView>(dialog).Any(), "角色资料表单");
                var roleForm = FindVisualDescendants<RoleProfileFormView>(dialog).Single();
                FindVisualDescendants<TextBox>(roleForm).Single(box => box.Text == "只读").Text = "查看角色";
                ((ButtonBase)roleForm.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => state.Role.RoleName == "查看角色"
                        && !FindVisualDescendants<RoleProfileFormView>(dialog).Any(), "角色资料保存");
                Assert.AreEqual("目标更新", state.User.NickName);
                Assert.AreEqual("查看角色", state.Role.RoleName);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>同用户撤销用户读取权后切到剩余目录页；账号切换强制关闭旧窗口。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RevocationSwitchesPageAndSessionCloses()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var userId = Guid.NewGuid().ToString();
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = userId,
                Permissions = ["System.Users.View", "System.Permissions.View"] });
            var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Users", dialog.CurrentPageName);
                currentUser.SetCurrentUser(new MeDto { UserId = userId,
                    Permissions = ["System.Permissions.View"] });
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Permissions", dialog.CurrentPageName);
                Assert.HasCount(0, model.Users);
                currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString() });
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.IsFalse(dialog.IsVisible);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>用户列表读取未完成时撤权，旧页结果被拒绝且加载结束后转到仍可读的目录页。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RevocationDuringLoadDefersAuthorizedNavigation()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var response = new TaskCompletionSource<ServiceResult<PagedResponseBase<ManagedUserDto>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var api = DispatchProxy.Create<ISystemRbacApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)api).Handler = (method, _) => method.Name switch
            {
                nameof(ISystemRbacApi.SearchUsersAsync) => WaitForUsers(),
                nameof(ISystemRbacApi.GetPermissionsAsync) => Task.FromResult(
                    ServiceResult<List<ManagedPermissionDto>>.Ok([])),
                _ => throw new NotSupportedException(method.Name)
            };
            Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> WaitForUsers()
            {
                started.TrySetResult();
                return response.Task;
            }
            var userId = Guid.NewGuid().ToString();
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = userId,
                Permissions = ["System.Users.View", "System.Permissions.View"] });
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                currentUser.SetCurrentUser(new MeDto { UserId = userId,
                    Permissions = ["System.Permissions.View"] });
                await DrainDispatcherAsync(dialog.Dispatcher);
                response.SetResult(ServiceResult<PagedResponseBase<ManagedUserDto>>.Ok(
                    new PagedResponseBase<ManagedUserDto> { Items = [new ManagedUserDto
                        { Uid = Guid.NewGuid(), UserName = "late" }], TotalCount = 1 }));
                await WaitForPageAsync(dialog, "Permissions", () => !model.IsBusy
                    && FindVisualDescendants<PermissionCatalogView>(dialog).Any());
                Assert.AreEqual("Permissions", dialog.CurrentPageName);
                Assert.IsNotNull(FindVisualDescendants<PermissionCatalogView>(dialog).SingleOrDefault());
                Assert.IsNull(FindVisualDescendants<UserManagementView>(dialog).SingleOrDefault());
                Assert.HasCount(0, model.Users);
            }
            finally
            {
                response.TrySetResult(ServiceResult<PagedResponseBase<ManagedUserDto>>.Fail("取消"));
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>未保存用户资料取消放弃时，侧栏切页保持原页和输入。</summary>
    [TestMethod]
    public void SystemSettingsWindow_CancelDiscardKeepsUserPage()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
            var api = DispatchProxy.Create<ISystemRbacApi,
                RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
            ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
            var reject = new RejectDialogService();
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, reject);
            var dialog = CreateSystemWindow(model, currentUser);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                model.UserNickName = "未保存昵称";
                ((ButtonBase)dialog.FindName("RolesNavigation")).RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.AreEqual("Users", dialog.CurrentPageName);
                Assert.AreEqual("未保存昵称", model.UserNickName);
                Assert.IsTrue(model.HasUnsavedChanges);
                Assert.IsTrue(((global::Wpf.Ui.Controls.NavigationViewItem)
                    dialog.FindName("UsersNavigation")).IsActive);
                Assert.IsFalse(((global::Wpf.Ui.Controls.NavigationViewItem)
                    dialog.FindName("RolesNavigation")).IsActive);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>本地弹窗拒绝放弃时保留密码，宿主强制关闭时擦除两类草稿。</summary>
    [TestMethod]
    public void FormDialogs_RejectDiscardThenForceCloseClearsPasswords()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var registration = await OpenRegistrationFormAsync();
            try
            {
                var password = (PasswordBox)registration.Form.FindName("PasswordInput");
                password.Password = "create-draft";
                FindVisualDescendants<ButtonBase>(registration.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(registration.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(registration.Window)
                        .Any(button => button.Content?.ToString() == "继续编辑" && button.IsVisible), "创建草稿放弃提示");
                FindVisualDescendants<ButtonBase>(registration.Window).Single(button =>
                    button.Content?.ToString() == "继续编辑" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.IsTrue(FindVisualDescendants<UserRegistrationView>(registration.Window).Any());
                Assert.AreEqual("create-draft", password.Password);
                registration.Window.ForceClose();
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (registration.Window.IsVisible) registration.Window.ForceClose();
                registration.Owner.Close();
            }
            var reset = await OpenResetFormAsync();
            try
            {
                var password = (PasswordBox)reset.Form.FindName("PasswordInput");
                password.Password = "reset-draft";
                FindVisualDescendants<ButtonBase>(reset.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(reset.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(reset.Window)
                        .Any(button => button.Content?.ToString() == "继续编辑" && button.IsVisible), "重置草稿放弃提示");
                FindVisualDescendants<ButtonBase>(reset.Window).Single(button =>
                    button.Content?.ToString() == "继续编辑" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.IsTrue(FindVisualDescendants<ResetPasswordView>(reset.Window).Any());
                Assert.AreEqual("reset-draft", password.Password);
                reset.Window.ForceClose();
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (reset.Window.IsVisible) reset.Window.ForceClose();
                reset.Owner.Close();
            }
        });
    }

    /// <summary>同一 ContentDialog 内确认放弃创建和重置草稿后关闭并擦除密码。</summary>
    [TestMethod]
    public void FormDialogs_AcceptDiscardClosesAndClearsPasswords()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var registration = await OpenRegistrationFormAsync();
            try
            {
                var password = (PasswordBox)registration.Form.FindName("PasswordInput");
                password.Password = "create-draft";
                FindVisualDescendants<ButtonBase>(registration.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(registration.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(registration.Window)
                        .Any(button => button.Content?.ToString() == "放弃变更" && button.IsVisible),
                    "创建草稿确认");
                FindVisualDescendants<ButtonBase>(registration.Window).Single(button =>
                    button.Content?.ToString() == "放弃变更" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(registration.Window.Dispatcher,
                    () => !FindVisualDescendants<UserRegistrationView>(registration.Window).Any(),
                    "创建草稿已放弃");
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (registration.Window.IsVisible) registration.Window.ForceClose();
                registration.Owner.Close();
            }
            var reset = await OpenResetFormAsync();
            try
            {
                var password = (PasswordBox)reset.Form.FindName("PasswordInput");
                password.Password = "reset-draft";
                FindVisualDescendants<ButtonBase>(reset.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(reset.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(reset.Window)
                        .Any(button => button.Content?.ToString() == "放弃变更" && button.IsVisible),
                    "重置草稿确认");
                FindVisualDescendants<ButtonBase>(reset.Window).Single(button =>
                    button.Content?.ToString() == "放弃变更" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(reset.Window.Dispatcher,
                    () => !FindVisualDescendants<ResetPasswordView>(reset.Window).Any(),
                    "重置草稿已放弃");
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (reset.Window.IsVisible) reset.Window.ForceClose();
                reset.Owner.Close();
            }
        });
    }

    /// <summary>服务端拒绝创建时表单与一次性密码保留，用户可修改后重试。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RegistrationFailureKeepsEditableInput()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, _) => method.Name == nameof(IAuthApi.RegisterAsync)
                    ? Task.FromResult(ServiceResult<Guid>.Fail("用户名已存在"))
                    : throw new NotSupportedException(method.Name);
            var host = await OpenRegistrationFormAsync(authApi);
            try
            {
                ((TextBox)host.Form.FindName("UserNameBox")).Text = "duplicate";
                var password = (PasswordBox)host.Form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)host.Form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(host.Form).Single(button =>
                    button.Content?.ToString() == "创建用户")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(host.Window.Dispatcher,
                    () => FindVisualDescendants<TextBlock>(host.Form)
                        .Any(item => item.Text.Contains("用户名已存在", StringComparison.Ordinal)),
                    "创建失败反馈");
                Assert.IsTrue(FindVisualDescendants<UserRegistrationView>(host.Window).Any());
                Assert.AreEqual("duplicate", ((TextBox)host.Form.FindName("UserNameBox")).Text);
                Assert.AreEqual("secret1", password.Password);
                Assert.AreEqual("secret1", ((PasswordBox)host.Form.FindName("ConfirmInput")).Password);
            }
            finally
            {
                if (host.Window.IsVisible) host.Window.ForceClose();
                host.Owner.Close();
            }
        });
    }

    /// <summary>服务端拒绝重置时目标固定，密码草稿仍可修改重试。</summary>
    [TestMethod]
    public void SystemSettingsWindow_ResetFailureKeepsPasswordInput()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, _) => method.Name == nameof(IAuthApi.ResetUserPasswordAsync)
                    ? Task.FromResult(ServiceResult<bool>.Fail("重置被拒绝"))
                    : throw new NotSupportedException(method.Name);
            var host = await OpenResetFormAsync(authApi);
            try
            {
                var target = ((TextBox)host.Form.FindName("TargetUserBox")).Text;
                var password = (PasswordBox)host.Form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)host.Form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(host.Form).Single(button =>
                    button.Content?.ToString() == "重置密码")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(host.Window.Dispatcher,
                    () => FindVisualDescendants<TextBlock>(host.Form)
                        .Any(item => item.Text.Contains("重置被拒绝", StringComparison.Ordinal)),
                    "重置失败反馈");
                Assert.IsTrue(FindVisualDescendants<ResetPasswordView>(host.Window).Any());
                Assert.AreEqual(target, ((TextBox)host.Form.FindName("TargetUserBox")).Text);
                Assert.AreEqual("secret1", password.Password);
                Assert.AreEqual("secret1", ((PasswordBox)host.Form.FindName("ConfirmInput")).Password);
            }
            finally
            {
                if (host.Window.IsVisible) host.Window.ForceClose();
                host.Owner.Close();
            }
        });
    }

    /// <summary>用户页从本地 ContentDialog 创建账号，成功后关闭并清除一次性密码。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RegistrationDialogReturnsCreatedUser()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            RegisterRequestDto? submitted = null;
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, args) => method.Name == nameof(IAuthApi.RegisterAsync)
                    ? Capture((RegisterRequestDto)args![0]!) : throw new NotSupportedException(method.Name);
            Task<ServiceResult<Guid>> Capture(RegisterRequestDto request)
            {
                submitted = request;
                return Task.FromResult(ServiceResult<Guid>.Ok(Guid.NewGuid()));
            }
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), IsAdmin = true,
                Permissions = ["System.Users.View"] });
            var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser, formApi: authApi);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show(); dialog.Owner = owner; dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                var page = FindVisualDescendants<UserManagementView>(dialog).Single();
                ((ButtonBase)page.FindName("CreateUserButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<UserRegistrationView>(dialog).Any(), "创建用户弹窗");
                var form = FindVisualDescendants<UserRegistrationView>(dialog).Single();
                Assert.AreSame(dialog, Window.GetWindow(form));
                ((TextBox)form.FindName("UserNameBox")).Text = "new-user";
                var password = (PasswordBox)form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "创建用户")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => submitted is not null && !FindVisualDescendants<UserRegistrationView>(dialog).Any(),
                    "创建用户完成");
                Assert.AreEqual("new-user", submitted?.UserName);
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>ContentDialog 创建提交期间禁用输入、拒绝普通取消，响应完成后才关闭。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RegistrationLocksModalFormUntilResponse()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var response = new TaskCompletionSource<ServiceResult<Guid>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, _) => method.Name == nameof(IAuthApi.RegisterAsync) ? Capture()
                    : throw new NotSupportedException(method.Name);
            Task<ServiceResult<Guid>> Capture() { started.TrySetResult(); return response.Task; }
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), IsAdmin = true,
                Permissions = ["System.Users.View"] });
            var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser, formApi: authApi);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show(); dialog.Owner = owner; dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                var page = FindVisualDescendants<UserManagementView>(dialog).Single();
                ((ButtonBase)page.FindName("CreateUserButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<UserRegistrationView>(dialog).Any(), "创建用户弹窗");
                var form = FindVisualDescendants<UserRegistrationView>(dialog).Single();
                ((TextBox)form.FindName("UserNameBox")).Text = "new-user";
                ((PasswordBox)form.FindName("PasswordInput")).Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "创建用户")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(((FrameworkElement)form.FindName("FormPanel")).IsEnabled);
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.IsTrue(FindVisualDescendants<UserRegistrationView>(dialog).Any());
                response.SetResult(ServiceResult<Guid>.Ok(Guid.NewGuid()));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => !FindVisualDescendants<UserRegistrationView>(dialog).Any(), "创建写入完成");
            }
            finally
            {
                response.TrySetResult(ServiceResult<Guid>.Fail("取消"));
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>行内重置入口固定目标，ContentDialog 成功后关闭并擦除密码。</summary>
    [TestMethod]
    public void SystemSettingsWindow_ResetDialogUsesSelectedUser()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
            var api = DispatchProxy.Create<ISystemRbacApi,
                RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
            ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
            AdminResetPasswordRequestDto? submitted = null;
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, args) => method.Name == nameof(IAuthApi.ResetUserPasswordAsync)
                    ? Capture((AdminResetPasswordRequestDto)args![0]!)
                    : throw new NotSupportedException(method.Name);
            Task<ServiceResult<bool>> Capture(AdminResetPasswordRequestDto request)
            {
                submitted = request;
                return Task.FromResult(ServiceResult<bool>.Ok(true));
            }
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View"] });
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser, formApi: authApi);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show(); dialog.Owner = owner; dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                var rowButton = FindVisualDescendants<ButtonBase>(dialog)
                    .Single(button => button.Content?.ToString() == "重置密码"
                        && button is FrameworkElement element
                        && element.DataContext is ManagedUserDto user && user.Uid == state.User.Uid);
                rowButton
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<ResetPasswordView>(dialog).Any(), "重置密码弹窗");
                var form = FindVisualDescendants<ResetPasswordView>(dialog).Single();
                Assert.AreSame(dialog, Window.GetWindow(form));
                Assert.AreEqual(state.User.UserName,
                    ((TextBox)form.FindName("TargetUserBox")).Text);
                var password = (PasswordBox)form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "重置密码")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => submitted is not null && !FindVisualDescendants<ResetPasswordView>(dialog).Any(),
                    "固定目标重置完成");
                Assert.AreEqual(state.User.Uid, submitted?.UserId);
                Assert.AreEqual(string.Empty, password.Password);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>创建表单仅填写邮箱也视为草稿；拒绝放弃保留，确认后关闭。</summary>
    [TestMethod]
    public void SystemSettingsWindow_EmailOnlyRegistrationDraftRequiresDiscard()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var dialog = await OpenRegistrationFormAsync();
            try
            {
                var email = FindVisualDescendants<TextBox>(dialog.Form).ElementAt(2);
                email.Text = "draft@example.test";
                FindVisualDescendants<ButtonBase>(dialog.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(dialog.Window)
                        .Any(button => button.Content?.ToString() == "继续编辑" && button.IsVisible),
                    "邮箱草稿放弃提示");
                FindVisualDescendants<ButtonBase>(dialog.Window).Single(button =>
                    button.Content?.ToString() == "继续编辑" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.AreEqual("draft@example.test", email.Text);
                FindVisualDescendants<ButtonBase>(dialog.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                FindVisualDescendants<ButtonBase>(dialog.Window).Single(button =>
                    button.Content?.ToString() == "放弃变更" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Window.Dispatcher,
                    () => !FindVisualDescendants<UserRegistrationView>(dialog.Window).Any(),
                    "邮箱草稿已放弃");
                Assert.AreEqual(string.Empty, email.Text);
            }
            finally
            {
                if (dialog.Window.IsVisible) dialog.Window.ForceClose();
                dialog.Owner.Close();
            }
        });
    }

    /// <summary>仅电话和仅确认密码也属于草稿，拒绝放弃后保留各自输入。</summary>
    [TestMethod]
    public void SystemSettingsWindow_RejectDiscardKeepsBothFormDrafts()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var registration = await OpenRegistrationFormAsync();
            try
            {
                var phone = FindVisualDescendants<TextBox>(registration.Form).ElementAt(3);
                phone.Text = "123456";
                FindVisualDescendants<ButtonBase>(registration.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(registration.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(registration.Window)
                        .Any(button => button.Content?.ToString() == "继续编辑" && button.IsVisible),
                    "电话草稿提示");
                FindVisualDescendants<ButtonBase>(registration.Window).Single(button =>
                    button.Content?.ToString() == "继续编辑" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.AreEqual("123456", phone.Text);
            }
            finally
            {
                if (registration.Window.IsVisible) registration.Window.ForceClose();
                registration.Owner.Close();
            }
            var reset = await OpenResetFormAsync();
            try
            {
                var confirm = (PasswordBox)reset.Form.FindName("ConfirmInput");
                confirm.Password = "confirm-only";
                FindVisualDescendants<ButtonBase>(reset.Form).Single(button =>
                    button.Content?.ToString() == "取消")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(reset.Window.Dispatcher,
                    () => FindVisualDescendants<ButtonBase>(reset.Window)
                        .Any(button => button.Content?.ToString() == "继续编辑" && button.IsVisible),
                    "确认密码草稿提示");
                FindVisualDescendants<ButtonBase>(reset.Window).Single(button =>
                    button.Content?.ToString() == "继续编辑" && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.AreEqual("confirm-only", confirm.Password);
            }
            finally
            {
                if (reset.Window.IsVisible) reset.Window.ForceClose();
                reset.Owner.Close();
            }
        });
    }

    /// <summary>本地创建弹窗写入途中会话切换，宿主和表单关闭、密码清除且迟到响应不重开。</summary>
    [TestMethod]
    public void SystemSettingsWindow_SessionSwitchClosesInFlightRegistration()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var response = new TaskCompletionSource<ServiceResult<Guid>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RegisterRequestDto? submitted = null;
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, args) => method.Name switch
                {
                    nameof(IAuthApi.RegisterAsync) => CaptureRegistration((RegisterRequestDto)args![0]!),
                    _ => throw new NotSupportedException(method.Name)
                };
            Task<ServiceResult<Guid>> CaptureRegistration(RegisterRequestDto request)
            {
                submitted = request;
                started.SetResult();
                return response.Task;
            }
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), IsAdmin = true,
                Permissions = ["System.Users.View"] });
            var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser, formApi: authApi);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                var view = FindVisualDescendants<UserManagementView>(dialog).Single();
                ((ButtonBase)view.FindName("CreateUserButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<UserRegistrationView>(dialog).Any(), "在途创建表单");
                var form = FindVisualDescendants<UserRegistrationView>(dialog).Single();
                ((TextBox)form.FindName("UserNameBox")).Text = "new-user";
                var password = (PasswordBox)form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "创建用户")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                currentUser.SetCurrentUser(new MeDto
                    { UserId = Guid.NewGuid().ToString(), IsAdmin = true });
                await WaitForStateAsync(dialog.Dispatcher, () => !dialog.IsVisible, "会话切换关闭宿主");
                Assert.AreEqual("new-user", submitted?.UserName);
                Assert.IsFalse(dialog.IsVisible);
                Assert.AreEqual(string.Empty, password.Password);
                response.SetResult(ServiceResult<Guid>.Ok(Guid.NewGuid()));
                await DrainDispatcherAsync(dialog.Dispatcher);
                Assert.IsFalse(FindVisualDescendants<UserRegistrationView>(dialog).Any());
                Assert.IsFalse(dialog.IsVisible);
            }
            finally
            {
                response.TrySetResult(ServiceResult<Guid>.Fail("取消"));
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>行内重置固定父页所选用户，打开表单后不另发全用户列表请求。</summary>
    [TestMethod]
    public void SystemSettingsWindow_ResetPasswordUsesSelectedUserAndClearsInput()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
            var api = DispatchProxy.Create<ISystemRbacApi,
                DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            var userSearches = 0;
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)api).Handler = (method, args) =>
            {
                if (method.Name == nameof(ISystemRbacApi.SearchUsersAsync)) userSearches++;
                return state.Call(method, args);
            };
            AdminResetPasswordRequestDto? submitted = null;
            var authApi = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
            ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)authApi).Handler =
                (method, args) => method.Name switch
                {
                    nameof(IAuthApi.ResetUserPasswordAsync) => CaptureReset((AdminResetPasswordRequestDto)args![0]!),
                    _ => throw new NotSupportedException(method.Name)
                };
            Task<ServiceResult<bool>> CaptureReset(AdminResetPasswordRequestDto request)
            {
                submitted = request;
                return Task.FromResult(ServiceResult<bool>.Ok(true));
            }
            var currentUser = new CurrentUserService();
            currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View"] });
            var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new UnusedAuthService(), currentUser, new AcceptDialogService());
            var dialog = CreateSystemWindow(model, currentUser, formApi: authApi);
            var owner = new global::Wpf.Ui.Controls.FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                dialog.Owner = owner;
                dialog.Show();
                await DrainDispatcherAsync(dialog.Dispatcher);
                var beforeOpen = userSearches;
                var rowButton = FindVisualDescendants<ButtonBase>(dialog)
                    .Single(button => button.Content?.ToString() == "重置密码"
                        && button is FrameworkElement element
                        && element.DataContext is ManagedUserDto user && user.Uid == state.User.Uid);
                rowButton
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => FindVisualDescendants<ResetPasswordView>(dialog).Any(), "固定目标重置表单");
                var form = FindVisualDescendants<ResetPasswordView>(dialog).Single();
                Assert.AreEqual(beforeOpen, userSearches);
                Assert.AreEqual(state.User.UserName,
                    ((TextBox)form.FindName("TargetUserBox")).Text);
                var password = (PasswordBox)form.FindName("PasswordInput");
                password.Password = "secret1";
                ((PasswordBox)form.FindName("ConfirmInput")).Password = "secret1";
                FindVisualDescendants<ButtonBase>(form).Single(button =>
                    button.Content?.ToString() == "重置密码")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await WaitForStateAsync(dialog.Dispatcher,
                    () => submitted is not null && !FindVisualDescendants<ResetPasswordView>(dialog).Any(),
                    "重置密码完成");
                Assert.AreEqual(state.User.Uid, submitted?.UserId);
                Assert.AreEqual(beforeOpen, userSearches);
                Assert.AreEqual(string.Empty, password.Password);
                Assert.AreEqual("Users", dialog.CurrentPageName);
            }
            finally
            {
                if (dialog.IsVisible) dialog.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>保存期间整个编辑表单锁定，避免已提交请求之后的新输入被成功关闭丢弃。</summary>
    [TestMethod]
    public void FormDialogs_PendingWriteDisablesAllRbacEditors()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var state = new RbacManagement.RbacManagementViewModelTests.ApiState { PausedMethod = "UserProfile" };
            var api = DispatchProxy.Create<ISystemRbacApi, RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
            ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
            var current = new CurrentUserService();
            current.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
                Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
            using var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
                new RefreshingAuthService(), current, new AcceptDialogService());
            await model.InitializeAsync();
            FrameworkElement[] forms = [new UserProfileFormView(model), new UserRolesFormView(model),
                new RoleProfileFormView(model, false), new RolePermissionsFormView(model)];
            model.UserNickName = "已提交资料";
            var saving = model.SaveUserProfileCommand.ExecuteAsync(null);
            try
            {
                await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await DrainDispatcherAsync(Dispatcher.CurrentDispatcher);
                foreach (var form in forms) Assert.IsFalse(form.IsEnabled, form.GetType().Name);
            }
            finally { state.Release.TrySetResult(); await saving; }
            await DrainDispatcherAsync(Dispatcher.CurrentDispatcher);
            foreach (var form in forms) Assert.IsTrue(form.IsEnabled, form.GetType().Name);
        });
    }

    /// <summary>弹窗搜索只作用于自己的集合视图，重新打开不会继承隐藏权限的筛选。</summary>
    [TestMethod]
    public void FormDialogs_PermissionSearchDoesNotLeakAcrossForms()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var current = new CurrentUserService();
            using var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
                new UnusedAuthService(), current, new AcceptDialogService());
            model.RolePermissionChoices.Add(new(Guid.NewGuid(), "销售查看"));
            model.RolePermissionChoices.Add(new(Guid.NewGuid(), "采购查看"));
            var first = new RolePermissionsFormView(model);
            ((TextBox)first.FindName("PermissionSearch")).Text = "销售";
            var second = new RolePermissionsFormView(model);
            await DrainDispatcherAsync(Dispatcher.CurrentDispatcher);
            Assert.AreEqual(1, ((ItemsControl)first.FindName("PermissionItems")).Items.Count);
            Assert.AreEqual(2, ((ItemsControl)second.FindName("PermissionItems")).Items.Count);
            Assert.AreEqual(2, model.RolePermissionChoices.Count);
        });
    }

    /// <summary>销售目录复用稳定键并统一分组，且公开集合不能被调用方篡改。</summary>
    [TestMethod]
    public void PermissionCatalog_SalesKeysAreStableAndCatalogIsReadOnly()
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

    private static SystemSettingsWindow CreateSystemWindow(RbacManagementViewModel model,
        CurrentUserService currentUser, IAuthApi? formApi = null)
        => new(() => model, () => throw new AssertFailedException("未授权读取字典页"),
            () => new UserRegistrationViewModel(formApi ?? CreateThrowingAuthApi(), new PassThroughApiCallRunner(),
                new WindowMessageService()),
            () => new ResetPasswordViewModel(formApi ?? CreateThrowingAuthApi(), new PassThroughApiCallRunner(),
                new WindowMessageService()),
            currentUser, new ContentDialogService(), new WindowMessageService());

    /// <summary>在真实系统设置宿主中打开创建表单，供取消和密码清理回归复用。</summary>
    private static async Task<(SystemSettingsWindow Window, global::Wpf.Ui.Controls.FluentWindow Owner,
        UserRegistrationView Form)> OpenRegistrationFormAsync(IAuthApi? formApi = null)
    {
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(), IsAdmin = true,
            Permissions = ["System.Users.View"] });
        var model = new RbacManagementViewModel(CreateEmptyRbacApi(), new PassThroughApiCallRunner(),
            new UnusedAuthService(), currentUser, new AcceptDialogService());
        var window = CreateSystemWindow(model, currentUser, formApi);
        var owner = new global::Wpf.Ui.Controls.FluentWindow
        { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        owner.Show(); window.Owner = owner; window.Show();
        await DrainDispatcherAsync(window.Dispatcher);
        var page = FindVisualDescendants<UserManagementView>(window).Single();
        ((ButtonBase)page.FindName("CreateUserButton"))
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitForStateAsync(window.Dispatcher,
            () => FindVisualDescendants<UserRegistrationView>(window).Any(), "创建用户弹窗");
        return (window, owner, FindVisualDescendants<UserRegistrationView>(window).Single());
    }

    /// <summary>从真实 DataGrid 行按钮打开固定目标重置表单。</summary>
    private static async Task<(SystemSettingsWindow Window, global::Wpf.Ui.Controls.FluentWindow Owner,
        ResetPasswordView Form)> OpenResetFormAsync(IAuthApi? formApi = null)
    {
        var state = new RbacManagement.RbacManagementViewModelTests.ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi,
            RbacManagement.RbacManagementViewModelTests.RbacApiProxy>();
        ((RbacManagement.RbacManagementViewModelTests.RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View"] });
        var model = new RbacManagementViewModel(api, new PassThroughApiCallRunner(),
            new UnusedAuthService(), currentUser, new AcceptDialogService());
        var window = CreateSystemWindow(model, currentUser, formApi);
        var owner = new global::Wpf.Ui.Controls.FluentWindow
        { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        owner.Show(); window.Owner = owner; window.Show();
        await DrainDispatcherAsync(window.Dispatcher);
        var rowButton = FindVisualDescendants<ButtonBase>(window)
            .Single(button => button.Content?.ToString() == "重置密码"
                && button is FrameworkElement element
                && element.DataContext is ManagedUserDto user && user.Uid == state.User.Uid);
        rowButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitForStateAsync(window.Dispatcher,
            () => FindVisualDescendants<ResetPasswordView>(window).Any(), "重置密码弹窗");
        return (window, owner, FindVisualDescendants<ResetPasswordView>(window).Single());
    }

    private sealed class RejectDialogService : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定",
            string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ShowErrorAsync(string title, string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>等待实际 UI 或写入状态达到目标，超时保留失败而非依赖固定 Dispatcher 次数。</summary>
    private static async Task WaitForStateAsync(Dispatcher dispatcher, Func<bool> ready, string description)
    {
        if (ready()) return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(20) };
        void OnTick(object? sender, EventArgs e) { if (ready()) completed.TrySetResult(); }
        timer.Tick += OnTick;
        try { timer.Start(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { Assert.Fail($"等待{description}超时。"); }
        finally { timer.Stop(); timer.Tick -= OnTick; }
    }

    /// <summary>等待权限变更后的目标页实际出现，超时报告最终状态而不依赖固定次数的 Dispatcher 空闲。</summary>
    private static async Task WaitForPageAsync(SystemSettingsWindow window, string expectedPage,
        Func<bool> isReady)
    {
        bool HasReachedFinalState() => window.CurrentPageName == expectedPage && isReady();
        if (HasReachedFinalState()) return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(20) };
        void OnTick(object? sender, EventArgs e)
        {
            if (HasReachedFinalState()) completed.TrySetResult();
        }
        timer.Tick += OnTick;
        try
        {
            timer.Start();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"等待系统设置页 {expectedPage} 超时；当前页：{window.CurrentPageName ?? "无"}。");
        }
        finally
        {
            timer.Stop();
            timer.Tick -= OnTick;
        }
    }

    private static IAuthApi CreateThrowingAuthApi()
    {
        var proxy = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)proxy).Handler =
            (method, _) => throw new NotSupportedException(method.Name);
        return proxy;
    }

    private static IAuthApi CreateAuthApiWithEmptyUserList()
    {
        var proxy = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)proxy).Handler =
            (method, _) => method.Name switch
            {
                nameof(IAuthApi.GetUsersAsync) => Task.FromResult(
                    ServiceResult<List<UserSummaryDto>>.Ok([])),
                _ => throw new NotSupportedException(method.Name)
            };
        return proxy;
    }

    private static ISystemRbacApi CreateEmptyRbacApi()
    {
        var proxy = DispatchProxy.Create<ISystemRbacApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)proxy).Handler = (method, _) => method.Name switch
        {
            nameof(ISystemRbacApi.GetPermissionsAsync) => Task.FromResult(ServiceResult<List<ManagedPermissionDto>>.Ok([])),
            nameof(ISystemRbacApi.SearchUsersAsync) => Task.FromResult(ServiceResult<PagedResponseBase<ManagedUserDto>>.Ok(
                new PagedResponseBase<ManagedUserDto> { PageIndex = 1, PageSize = 100 })),
            nameof(ISystemRbacApi.SearchRolesAsync) => Task.FromResult(ServiceResult<PagedResponseBase<ManagedRoleDto>>.Ok(
                new PagedResponseBase<ManagedRoleDto> { PageIndex = 1, PageSize = 100 })),
            _ => throw new NotSupportedException(method.Name)
        };
        return proxy;
    }

    private sealed class UnusedAuthService : IAuthService
    {
        public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync() => throw new NotSupportedException();
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password) => throw new NotSupportedException();
        public Task<bool> TryRestoreSessionAsync() => throw new NotSupportedException();
    }

    /// <summary>模拟管理写入后的本人权限刷新，使 STA 测试聚焦视图绑定。</summary>
    private sealed class RefreshingAuthService : IAuthService
    {
        /// <summary>确认保存后刷新当前会话的调用完成。</summary>
        public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync()
            => Task.FromResult(ServiceResult<MeDto>.Ok(new MeDto()));
        /// <summary>本窗口交互测试不执行登录。</summary>
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password) => throw new NotSupportedException();
        /// <summary>本窗口交互测试不执行会话恢复。</summary>
        public Task<bool> TryRestoreSessionAsync() => throw new NotSupportedException();
    }

    private sealed class FakeSettingsWindowService : ISettingsWindowService
    {
        public void ShowChangePassword() { }
        public void ShowSystemSettings() { }
    }

    private sealed class UnusedApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败")
            => throw new NotSupportedException();

        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败")
            => throw new NotSupportedException();

        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => throw new NotSupportedException();
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            string failureMessage = "请求失败") => action();

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

    private sealed class AcceptDialogService : IDialogService
    {
        public Task ShowContentAsync(
            string title,
            object content,
            string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ShowErrorAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionService : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason)
            => SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}

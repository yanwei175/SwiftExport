using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>在真实 WPF STA 和应用资源下验证设置中心的键盘焦点与无障碍名称。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowInteractionTests
{
    /// <summary>验证五个入口具备中文名称并能按 Tab 顺序移动焦点。</summary>
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
                Permissions = [PermissionKeys.SystemSettingsManage]
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
                Assert.AreEqual(5, buttons.Count);
                CollectionAssert.AreEqual(
                    new[] { "修改我的密码", "创建用户", "重置用户密码", "模块权限", "字典管理" },
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

    private static IAuthApi CreateThrowingAuthApi()
    {
        var proxy = DispatchProxy.Create<IAuthApi, DictionaryManagerLifecycleTests.DelegateDispatchProxy>();
        ((DictionaryManagerLifecycleTests.DelegateDispatchProxy)(object)proxy).Handler =
            (method, _) => throw new NotSupportedException(method.Name);
        return proxy;
    }

    private sealed class FakeSettingsWindowService : ISettingsWindowService
    {
        public void ShowChangePassword() { }
        public void ShowUserRegistration() { }
        public void ShowResetPassword() { }
        public void ShowUserModules() { }
        public void ShowDictionaries() { }
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

    private sealed class FakeSessionService : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason)
            => SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}

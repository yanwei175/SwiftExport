using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Authentication;

/// <summary>验证登录成功会结束模态 LoginWindow，使 App 可以继续显示 Shell。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LoginWindowLifecycleTests
{
    /// <summary>验证完整认证成功后窗口关闭并返回明确成功状态。</summary>
    [TestMethod]
    public void SuccessfulLogin_ShouldCloseDialogAndExposeSuccessOutcome()
    {
        DatePickerAssistTests.RunWithApplication(() =>
        {
            ApplicationResourceLoader.Load();
            var viewModel = new LoginViewModel(new SuccessfulAuthService())
            {
                UserName = "tester"
            };
            var window = new LoginWindow(viewModel);
            var timedOut = false;
            DispatcherTimer? timeout = null;
            timeout = new DispatcherTimer(
                TimeSpan.FromSeconds(3),
                DispatcherPriority.Normal,
                (_, _) =>
                {
                    timedOut = true;
                    timeout!.Stop();
                    window.Close();
                },
                window.Dispatcher);

            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var passwordBox = FindVisualDescendants<global::Wpf.Ui.Controls.PasswordBox>(window).Single();
                passwordBox.Password = "secret";
                var loginButton = FindVisualDescendants<ButtonBase>(window)
                    .Single(button => string.Equals(button.Content?.ToString(), "登录", StringComparison.Ordinal));
                loginButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }));

            timeout.Start();
            window.ShowDialog();
            timeout.Stop();

            Assert.IsFalse(timedOut, "登录成功后 LoginWindow 没有及时结束模态循环。");
            Assert.IsTrue(window.IsLoginSuccessful);
            Assert.IsFalse(window.IsVisible);
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

    private sealed class SuccessfulAuthService : IAuthService
    {
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password)
            => Task.FromResult(ServiceResult<MeDto>.Ok(new MeDto
            {
                UserId = Guid.NewGuid().ToString(),
                UserName = userName
            }));

        public Task<bool> TryRestoreSessionAsync() => Task.FromResult(false);
    }
}

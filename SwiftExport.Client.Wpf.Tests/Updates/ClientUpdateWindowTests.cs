using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Updates;

namespace SwiftExport.Client.Wpf.Tests.Updates;

/// <summary>在真实 STA、主题和 WPF 命令绑定下验证更新窗口状态与关闭。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ClientUpdateWindowTests
{
    /// <summary>下载按钮通过真实命令改变进度与重启准入，键盘能够到达后续操作。</summary>
    [TestMethod]
    public void Window_BindsCommandsProgressAndKeyboardFocus()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            using var model = ClientUpdateTests.Create(new());
            var window = new ClientUpdateWindow(model) { ShowInTaskbar = false };
            try
            {
                window.Show();
                await DrainAsync(window);
                var download = (Button)window.FindName("DownloadButton");
                var restart = (Button)window.FindName("RestartButton");
                Assert.IsTrue(download.IsEnabled);
                Assert.IsFalse(restart.IsEnabled);
                Assert.IsTrue(download.Focus());
                await model.DownloadCommand.ExecuteAsync(null);
                await DrainAsync(window);
                Assert.IsTrue(restart.IsEnabled);
                Assert.AreEqual(100d, ((ProgressBar)window.FindName("DownloadProgressBar")).Value);
                Assert.AreEqual(model.Message, ((TextBlock)window.FindName("UpdateStatus")).Text);
                Assert.IsTrue(restart.Focus());
                Assert.IsTrue(restart.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.AreSame(window.FindName("LaterButton"), Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>关闭窗口取消下载，迟到完成不能开放重启。</summary>
    [TestMethod]
    public void WindowClose_CancelsActiveDownload()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            using var model = ClientUpdateTests.Create(new() { WaitForCancellation = true });
            var window = new ClientUpdateWindow(model) { ShowInTaskbar = false };
            try
            {
                window.Show();
                await DrainAsync(window);
                var download = model.DownloadCommand.ExecuteAsync(null);
                window.Close();
                await download;
                Assert.IsFalse(model.IsBusy);
                Assert.IsFalse(model.IsReadyToRestart);
                Assert.IsNotNull(model.AvailableRelease);
            }
            finally { if (window.IsVisible) window.Close(); }
        });
    }

    private static Task DrainAsync(Window window) => window.Dispatcher.InvokeAsync(
        () => { }, DispatcherPriority.ApplicationIdle).Task;
}

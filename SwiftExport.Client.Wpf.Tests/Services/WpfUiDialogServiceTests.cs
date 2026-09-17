using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Services.UI;
using Wpf.Ui;
using Wpf.Ui.Controls;
using PublicDialogResult = SwiftExportClient.Services.UI.DialogResult;
using WpfDialogResult = Wpf.Ui.Controls.ContentDialogResult;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>验证公共 DialogService 的结果、布局、关闭保护、并发和资源释放。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WpfUiDialogServiceTests
{
    /// <summary>真实 WPF-UI 模板在比例占满时应保留外围留白，并让标题和按钮处于可见外壳内。</summary>
    [TestMethod]
    public void RatioSize_RealTemplate_PreservesOuterMarginInsideHost() =>
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());

            var host = new ContentDialogHost();
            var window = new Window
            {
                Width = 1000,
                Height = 700,
                Content = host,
                WindowStyle = WindowStyle.None
            };
            var nativeService = new ContentDialogService();
            nativeService.SetDialogHost(host);
            var service = new WpfUiDialogService(nativeService);

            try
            {
                window.Show();
                var pending = service.ShowAsync(
                    new Grid(),
                    new DialogOptions("尺寸复现")
                    {
                        WidthRatio = 1,
                        HeightRatio = 1,
                        ScrollStrategy = DialogScrollStrategy.Content
                    });

                await host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var dialog = (ContentDialog)host.Content!;
                dialog.UpdateLayout();
                var shell = FindDialogShell(dialog);
                var shellOrigin = shell.TranslatePoint(new Point(), host);
                var margin = dialog.DialogMargin;

                // MSTest 比较断言先传边界、后传实际值；命名参数避免把包含关系反转。
                Assert.IsGreaterThanOrEqualTo(lowerBound: margin.Left - 0.5, value: shellOrigin.X);
                Assert.IsGreaterThanOrEqualTo(lowerBound: margin.Top - 0.5, value: shellOrigin.Y);
                Assert.IsLessThanOrEqualTo(
                    upperBound: host.ActualWidth - margin.Right + 0.5,
                    value: shellOrigin.X + shell.ActualWidth);
                Assert.IsLessThanOrEqualTo(
                    upperBound: host.ActualHeight - margin.Bottom + 0.5,
                    value: shellOrigin.Y + shell.ActualHeight);

                var title = FindVisualDescendant<ContentPresenter>(
                    shell,
                    presenter => Equals(presenter.Content, dialog.Title));
                var closeButton = dialog.Template.FindName("CloseButton", dialog) as FrameworkElement;
                Assert.IsNotNull(title);
                Assert.IsNotNull(closeButton);
                Assert.IsGreaterThan(lowerBound: 0, value: title.ActualHeight);
                Assert.IsGreaterThan(lowerBound: 0, value: closeButton.ActualHeight);
                AssertInsideShell(title, shell);
                AssertInsideShell(closeButton, shell);

                dialog.Hide();
                _ = await pending;
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>内容区主动确认应复用原生 Primary 结果，并在弹窗结束后解除可选事件订阅。</summary>
    [TestMethod]
    public void CloseRequest_ShouldReturnPrimaryAndReleaseSubscription() =>
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
            var host = new ContentDialogHost();
            var window = new Window { Width = 800, Height = 500, Content = host, WindowStyle = WindowStyle.None };
            var nativeService = new ContentDialogService();
            nativeService.SetDialogHost(host);
            var service = new WpfUiDialogService(nativeService);
            var state = new TestDialogState();

            try
            {
                window.Show();
                var pending = service.ShowAsync(
                    new Grid(),
                    new DialogOptions("主动确认") { PrimaryButtonText = "确定", CloseButtonText = "取消" },
                    state);
                await host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                state.RaiseCloseRequested(PublicDialogResult.Primary);
                var result = await pending;

                Assert.AreEqual(PublicDialogResult.Primary, result);
                Assert.AreEqual(1, state.CloseRequestAddCount);
                Assert.AreEqual(1, state.CloseRequestRemoveCount);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>结构化弹窗应应用按钮、有限宿主尺寸、正文滚动和动态状态，并在完成后释放订阅。</summary>
    [TestMethod]
    public void ShowAsync_AppliesOptionsBoundsScrollAndState_ThenReleases() => StaTest.RunAsync(async () =>
    {
        var host = CreateHost(1000, 700);
        var state = new TestDialogState { PrimaryEnabled = false, SecondaryEnabled = true };
        ContentDialog? captured = null;
        var service = new WpfUiDialogService(CreateContentDialogService(host, (dialog, _) =>
        {
            captured = dialog;
            Assert.AreEqual("选择候选", dialog.Title);
            Assert.AreEqual("确定", dialog.PrimaryButtonText);
            Assert.AreEqual("继续", dialog.SecondaryButtonText);
            Assert.AreEqual("取消", dialog.CloseButtonText);
            Assert.AreEqual(ContentDialogButton.Primary, dialog.DefaultButton);
            Assert.AreEqual(1000, dialog.DialogWidth);
            Assert.AreEqual(700, dialog.DialogHeight);
            Assert.AreEqual(1000, dialog.DialogMaxWidth);
            Assert.AreEqual(700, dialog.DialogMaxHeight);
            Assert.AreEqual(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(dialog));
            Assert.AreEqual(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(dialog));
            Assert.IsFalse(dialog.IsPrimaryButtonEnabled);
            Assert.IsTrue(dialog.IsSecondaryButtonEnabled);

            state.PrimaryEnabled = true;
            state.RaiseChanged();
            Assert.IsTrue(dialog.IsPrimaryButtonEnabled);

            state.AllowClose = false;
            var blocked = new ContentDialogClosingEventArgs(ContentDialog.ClosingEvent, dialog)
            {
                Result = WpfDialogResult.Primary
            };
            dialog.RaiseEvent(blocked);
            Assert.IsTrue(blocked.Cancel);
            Assert.AreEqual(PublicDialogResult.Primary, state.LastRequestedResult);

            state.AllowClose = true;
            host.Width = 640;
            host.Height = 480;
            host.Measure(new Size(640, 480));
            host.Arrange(new Rect(0, 0, 640, 480));
            // 手动 Arrange 后完成布局周期，才能发送真实 SizeChanged 并验证服务订阅。
            host.UpdateLayout();
            Assert.AreEqual(640, dialog.DialogMaxWidth);
            Assert.AreEqual(480, dialog.DialogMaxHeight);
            return Task.FromResult(WpfDialogResult.Secondary);
        }));

        var content = new Grid();
        var result = await service.ShowAsync(
            content,
            new DialogOptions("选择候选")
            {
                PrimaryButtonText = "确定",
                SecondaryButtonText = "继续",
                CloseButtonText = "取消",
                DefaultButton = DialogButton.Primary,
                Width = 1200,
                Height = 900,
                MaxWidth = 1400,
                MaxHeight = 850,
                ScrollStrategy = DialogScrollStrategy.Content
            },
            state);

        Assert.AreEqual(PublicDialogResult.Secondary, result);
        Assert.AreEqual(1, state.AddCount);
        Assert.AreEqual(1, state.RemoveCount);
        Assert.IsNull(captured!.Content);

        state.PrimaryEnabled = false;
        state.RaiseChanged();
        host.Width = 320;
        host.Height = 240;
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));
        host.UpdateLayout();
        Assert.IsTrue(captured.IsPrimaryButtonEnabled, "完成后不应再接收状态变化。");
        Assert.AreEqual(640, captured.DialogMaxWidth, "完成后不应再接收宿主尺寸事件。");
        Assert.AreEqual(480, captured.DialogMaxHeight);
    });

    /// <summary>旧方法应继续使用原按钮默认值，并保持 Primary 到 bool 的映射。</summary>
    [TestMethod]
    public void LegacyMethods_PreserveSignaturesAndResultMapping() => StaTest.RunAsync(async () =>
    {
        var host = CreateHost(900, 600);
        var results = new Queue<WpfDialogResult>([
            WpfDialogResult.Primary,
            WpfDialogResult.None,
            WpfDialogResult.None,
            WpfDialogResult.None]);
        var dialogs = new List<(object? Title, string? Primary, string? Close, ContentDialogButton Default)>();
        var service = new WpfUiDialogService(CreateContentDialogService(host, (dialog, _) =>
        {
            dialogs.Add((dialog.Title, dialog.PrimaryButtonText, dialog.CloseButtonText, dialog.DefaultButton));
            Assert.IsFalse(dialog.IsSecondaryButtonEnabled, "未配置次按钮文字时必须隐藏次按钮。");
            Assert.AreEqual(!string.IsNullOrWhiteSpace(dialog.PrimaryButtonText), dialog.IsPrimaryButtonEnabled,
                "只有关闭按钮的提示弹窗不得显示空白主按钮。");
            Assert.AreSame(DependencyProperty.UnsetValue,
                dialog.ReadLocalValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
            Assert.AreSame(DependencyProperty.UnsetValue,
                dialog.ReadLocalValue(ContentDialog.DialogWidthProperty));
            Assert.AreSame(DependencyProperty.UnsetValue,
                dialog.ReadLocalValue(ContentDialog.DialogHeightProperty));
            Assert.AreEqual(900, dialog.DialogMaxWidth, "未指定宽度时仍应受当前宿主限制。");
            Assert.AreEqual(600, dialog.DialogMaxHeight, "未指定高度时仍应受当前宿主限制。");
            return Task.FromResult(results.Dequeue());
        }));

        Assert.IsTrue(await service.ConfirmAsync("确认一", "内容", "执行", "返回"));
        Assert.IsFalse(await service.ConfirmAsync("确认二", "内容"));
        await service.ShowContentAsync("内容标题", "正文", "知道了");
        await service.ShowErrorAsync("错误标题", "错误正文");

        Assert.AreEqual(("确认一", "执行", "返回", ContentDialogButton.Close), dialogs[0]);
        Assert.AreEqual(("确认二", "确定", "取消", ContentDialogButton.Close), dialogs[1]);
        Assert.AreEqual(("内容标题", string.Empty, "知道了", ContentDialogButton.Close), dialogs[2]);
        Assert.AreEqual(("错误标题", string.Empty, "关闭", ContentDialogButton.Close), dialogs[3]);
    });

    /// <summary>活动弹窗期间应拒绝第二次展示；取消后仍须释放状态订阅并允许后续调用。</summary>
    [TestMethod]
    public void CancellationAndConcurrency_ReleaseAllOwnedState() => StaTest.RunAsync(async () =>
    {
        var host = CreateHost(800, 500);
        var firstCompletion = new TaskCompletionSource<WpfDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var state = new TestDialogState();
        var service = new WpfUiDialogService(CreateContentDialogService(host, (_, _) =>
        {
            calls++;
            return calls == 1 ? firstCompletion.Task : Task.FromResult(WpfDialogResult.None);
        }));

        using var cancellation = new CancellationTokenSource();
        var pending = service.ShowAsync("第一个", new DialogOptions("第一个"), state, cancellation.Token);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ShowAsync("第二个", new DialogOptions("第二个")));

        cancellation.Cancel();
        firstCompletion.TrySetCanceled(cancellation.Token);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        Assert.AreEqual(1, state.AddCount);
        Assert.AreEqual(1, state.RemoveCount);

        Assert.AreEqual(PublicDialogResult.Close,
            await service.ShowAsync("第三个", new DialogOptions("第三个")));
        Assert.AreEqual(2, calls);
    });

    /// <summary>无宿主及非法尺寸应在展示前给出稳定异常，避免落入 WPF 依赖属性错误。</summary>
    [TestMethod]
    public void InvalidHostAndDimensions_AreRejectedBeforeShow() => StaTest.RunAsync(async () =>
    {
        var missingHost = new WpfUiDialogService(CreateContentDialogService(null, (_, _) =>
            throw new AssertFailedException("无宿主时不应调用 ShowAsync。")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => missingHost.ShowAsync("正文", new DialogOptions("标题")));

        var host = CreateHost(800, 500);
        var service = new WpfUiDialogService(CreateContentDialogService(host, (_, _) =>
            throw new AssertFailedException("非法尺寸不应进入 ShowAsync。")));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { Width = 0 }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { MaxHeight = double.PositiveInfinity }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { WidthRatio = 0 }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { HeightRatio = double.NaN }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { WidthRatio = 1.01 }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.ShowAsync(
                "正文",
                new DialogOptions("标题") { Width = 500, WidthRatio = 0.8 }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.ShowAsync(
                "正文",
                new DialogOptions("标题") { Height = 400, HeightRatio = 0.8 }));
    });

    /// <summary>显式宽高应同时约束 WPF-UI 外壳的最小和最大尺寸，并在宿主缩小时同步收缩。</summary>
    [TestMethod]
    public void ExplicitDimensions_FixShellSizeWithinCurrentHost() => StaTest.RunAsync(() =>
    {
        var host = CreateHost(1000, 700);
        var dialog = new ContentDialog { DialogMargin = new Thickness(35) };
        var shell = new Border();
        var options = new DialogOptions("固定尺寸")
        {
            Width = 1200,
            Height = 900,
            MaxWidth = 1400,
            MaxHeight = 850
        };

        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(930, dialog.DialogWidth);
        Assert.AreEqual(630, dialog.DialogHeight);
        Assert.AreEqual(930, shell.MinWidth);
        Assert.AreEqual(630, shell.MinHeight);
        Assert.AreEqual(930, shell.MaxWidth);
        Assert.AreEqual(630, shell.MaxHeight);

        host.Width = 640;
        host.Height = 480;
        host.Measure(new Size(640, 480));
        host.Arrange(new Rect(0, 0, 640, 480));
        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(570, dialog.DialogWidth);
        Assert.AreEqual(410, dialog.DialogHeight);
        Assert.AreEqual(570, shell.MinWidth);
        Assert.AreEqual(410, shell.MinHeight);
        Assert.AreEqual(570, shell.MaxWidth);
        Assert.AreEqual(410, shell.MaxHeight);
        return Task.CompletedTask;
    });

    /// <summary>比例尺寸应基于扣除外围留白后的宿主空间，并受显式最大值约束且可随宿主恢复。</summary>
    [TestMethod]
    public void RatioDimensions_UseAvailableHostSpaceAndRestoreAfterResize() => StaTest.RunAsync(() =>
    {
        var host = CreateHost(1000, 700);
        var dialog = new ContentDialog { DialogMargin = new Thickness(35) };
        var shell = new Border();
        var options = new DialogOptions("比例尺寸")
        {
            WidthRatio = 0.8,
            HeightRatio = 0.5,
            MaxWidth = 700,
            MaxHeight = 400
        };

        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(700, dialog.DialogWidth);
        Assert.AreEqual(315, dialog.DialogHeight);
        Assert.AreEqual(700, shell.MinWidth);
        Assert.AreEqual(315, shell.MinHeight);

        host.Width = 600;
        host.Height = 400;
        host.Measure(new Size(600, 400));
        host.Arrange(new Rect(0, 0, 600, 400));
        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(424, dialog.DialogWidth, 0.001);
        Assert.AreEqual(165, dialog.DialogHeight, 0.001);

        host.Width = 1000;
        host.Height = 700;
        host.Measure(new Size(1000, 700));
        host.Arrange(new Rect(0, 0, 1000, 700));
        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(700, dialog.DialogWidth);
        Assert.AreEqual(315, dialog.DialogHeight);

        ApplyHostBounds(
            dialog,
            shell,
            host,
            new DialogOptions("混合尺寸") { Width = 500, HeightRatio = 0.5 });
        Assert.AreEqual(500, dialog.DialogWidth);
        Assert.AreEqual(315, dialog.DialogHeight);
        return Task.CompletedTask;
    });

    /// <summary>宿主尚未测量时应延迟比例固定值，极小宿主则只产生非负有限边界。</summary>
    [TestMethod]
    public void RatioDimensions_HandleUnmeasuredAndTinyHostWithoutInvalidValues() => StaTest.RunAsync(() =>
    {
        var host = new ContentDialogHost();
        var dialog = new ContentDialog { DialogMargin = new Thickness(35) };
        var shell = new Border();
        var options = new DialogOptions("边界尺寸") { WidthRatio = 1, HeightRatio = 1 };

        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreSame(
            DependencyProperty.UnsetValue,
            dialog.ReadLocalValue(ContentDialog.DialogWidthProperty));
        Assert.AreSame(
            DependencyProperty.UnsetValue,
            dialog.ReadLocalValue(ContentDialog.DialogHeightProperty));

        host.Width = 50;
        host.Height = 40;
        host.Measure(new Size(50, 40));
        host.Arrange(new Rect(0, 0, 50, 40));
        ApplyHostBounds(dialog, shell, host, options);
        Assert.AreEqual(0, dialog.DialogWidth);
        Assert.AreEqual(0, dialog.DialogHeight);
        Assert.AreEqual(0, shell.MinWidth);
        Assert.AreEqual(0, shell.MinHeight);
        Assert.AreEqual(0, shell.MaxWidth);
        Assert.AreEqual(0, shell.MaxHeight);
        return Task.CompletedTask;
    });

    private static ContentDialogHost CreateHost(double width, double height)
    {
        var host = new ContentDialogHost { Width = width, Height = height };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        return host;
    }

    /// <summary>取得真实 WPF-UI 模板中的可见弹窗外壳。</summary>
    private static FrameworkElement FindDialogShell(ContentDialog dialog)
    {
        dialog.ApplyTemplate();
        var root = dialog.Template.FindName("DialogRootGrid", dialog) as Grid;
        Assert.IsNotNull(root);
        Assert.HasCount(1, root.Children);
        return (FrameworkElement)root.Children[0];
    }

    /// <summary>确认标题或按钮的布局矩形完全位于可见外壳中。</summary>
    private static void AssertInsideShell(FrameworkElement child, FrameworkElement shell)
    {
        var origin = child.TranslatePoint(new Point(), shell);
        Assert.IsGreaterThanOrEqualTo(lowerBound: -0.5, value: origin.X);
        Assert.IsGreaterThanOrEqualTo(lowerBound: -0.5, value: origin.Y);
        Assert.IsLessThanOrEqualTo(upperBound: shell.ActualWidth + 0.5, value: origin.X + child.ActualWidth);
        Assert.IsLessThanOrEqualTo(upperBound: shell.ActualHeight + 0.5, value: origin.Y + child.ActualHeight);
    }

    /// <summary>按类型和条件查找模板中的首个可视元素。</summary>
    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && predicate(match))
                return match;

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    /// <summary>调用私有布局映射，直接验证 WPF-UI 模板外壳的尺寸契约。</summary>
    private static void ApplyHostBounds(
        ContentDialog dialog,
        FrameworkElement shell,
        ContentDialogHost host,
        DialogOptions options)
    {
        var method = typeof(WpfUiDialogService).GetMethod(
            "ApplyHostBounds",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        method.Invoke(null, [dialog, shell, host, options]);
    }

    private static IContentDialogService CreateContentDialogService(
        ContentDialogHost? host,
        Func<ContentDialog, CancellationToken, Task<WpfDialogResult>> show)
    {
        var proxy = DispatchProxy.Create<IContentDialogService, DialogServiceProxy>();
        ((DialogServiceProxy)(object)proxy).Handler = (method, arguments) => method.Name switch
        {
            nameof(IContentDialogService.GetDialogHostEx) => host,
            nameof(IContentDialogService.ShowAsync) => show(
                (ContentDialog)arguments![0]!,
                (CancellationToken)arguments[1]!),
            _ => throw new NotSupportedException(method.Name)
        };
        return proxy;
    }

    /// <summary>为测试提供只实现所需方法的 IContentDialogService 动态替身。</summary>
    public class DialogServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    /// <summary>记录公共服务是否对动态状态完成成对订阅。</summary>
    private sealed class TestDialogState : IDialogContentState, IDialogCloseRequest
    {
        private EventHandler? _stateChanged;

        public event EventHandler? StateChanged
        {
            add { AddCount++; _stateChanged += value; }
            remove { RemoveCount++; _stateChanged -= value; }
        }

        private EventHandler<DialogCloseRequestedEventArgs>? _closeRequested;
        public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested
        {
            add { CloseRequestAddCount++; _closeRequested += value; }
            remove { CloseRequestRemoveCount++; _closeRequested -= value; }
        }

        public bool PrimaryEnabled { get; set; } = true;
        public bool SecondaryEnabled { get; set; } = true;
        public bool AllowClose { get; set; } = true;
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public int CloseRequestAddCount { get; private set; }
        public int CloseRequestRemoveCount { get; private set; }
        public PublicDialogResult? LastRequestedResult { get; private set; }
        public bool IsPrimaryButtonEnabled => PrimaryEnabled;
        public bool IsSecondaryButtonEnabled => SecondaryEnabled;

        public bool CanClose(PublicDialogResult requestedResult)
        {
            LastRequestedResult = requestedResult;
            return AllowClose;
        }

        public void RaiseChanged() => _stateChanged?.Invoke(this, EventArgs.Empty);
        public void RaiseCloseRequested(PublicDialogResult result) =>
            _closeRequested?.Invoke(this, new DialogCloseRequestedEventArgs(result));
    }
}

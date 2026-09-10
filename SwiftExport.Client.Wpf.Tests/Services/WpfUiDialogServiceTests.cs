using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
public sealed class WpfUiDialogServiceTests
{
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
            Assert.AreSame(DependencyProperty.UnsetValue,
                dialog.ReadLocalValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
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
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.ShowAsync("第二个", new DialogOptions("第二个")));

        cancellation.Cancel();
        firstCompletion.TrySetCanceled(cancellation.Token);
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await pending);
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
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => missingHost.ShowAsync("正文", new DialogOptions("标题")));

        var host = CreateHost(800, 500);
        var service = new WpfUiDialogService(CreateContentDialogService(host, (_, _) =>
            throw new AssertFailedException("非法尺寸不应进入 ShowAsync。")));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { Width = 0 }));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
            () => service.ShowAsync("正文", new DialogOptions("标题") { MaxHeight = double.PositiveInfinity }));
    });

    private static ContentDialogHost CreateHost(double width, double height)
    {
        var host = new ContentDialogHost { Width = width, Height = height };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        return host;
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
    private sealed class DialogServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    /// <summary>记录公共服务是否对动态状态完成成对订阅。</summary>
    private sealed class TestDialogState : IDialogContentState
    {
        private EventHandler? _stateChanged;

        public event EventHandler? StateChanged
        {
            add { AddCount++; _stateChanged += value; }
            remove { RemoveCount++; _stateChanged -= value; }
        }

        public bool PrimaryEnabled { get; set; } = true;
        public bool SecondaryEnabled { get; set; } = true;
        public bool AllowClose { get; set; } = true;
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public PublicDialogResult? LastRequestedResult { get; private set; }
        public bool IsPrimaryButtonEnabled => PrimaryEnabled;
        public bool IsSecondaryButtonEnabled => SecondaryEnabled;

        public bool CanClose(PublicDialogResult requestedResult)
        {
            LastRequestedResult = requestedResult;
            return AllowClose;
        }

        public void RaiseChanged() => _stateChanged?.Invoke(this, EventArgs.Empty);
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>验证字典 ViewModel 作为独立窗口内容时的迟到结果隔离和未保存关闭门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DictionaryManagerLifecycleTests
{
    /// <summary>验证独立窗口提供关闭标题栏，且字典内容位于标题栏下方。</summary>
    [TestMethod]
    public void Window_ShouldExposeCloseTitleBarWithoutOverlappingDictionaryContent()
    {
        DatePickerAssistTests.RunWithApplication(() =>
        {
            ApplicationResourceLoader.Load();
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
            {
                nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(
                    ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([])),
                _ => throw new NotSupportedException(method.Name)
            });
            var viewModel = CreateViewModel(
                typeApi,
                CreateThrowingProxy<ISysDictDataApi>(),
                new FakeDialogService(true));
            var owner = new FluentWindow
            {
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            var window = new DictionaryManagerWindow(
                viewModel,
                new ContentDialogService(),
                new WindowMessageService());
            var titleBarFound = false;
            var closeButtonVisible = false;
            var contentStartsBelowTitleBar = false;

            try
            {
                owner.Show();
                window.Owner = owner;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var titleBar = FindVisualDescendants<TitleBar>(window).Single();
                    var dictionaryView = (DictionaryManagerView)window.FindName("DictionaryView");
                    titleBarFound = true;
                    closeButtonVisible = titleBar.ShowClose;
                    contentStartsBelowTitleBar = dictionaryView.TranslatePoint(new Point(), window).Y
                        >= titleBar.TranslatePoint(new Point(0, titleBar.ActualHeight), window).Y;
                    // 真实关闭入口必须先取消当前 Closing，再排队完成第二次关闭，避免窗口关闭重入。
                    window.Close();
                }));

                window.ShowDialog();

                Assert.IsTrue(titleBarFound);
                Assert.IsTrue(closeButtonVisible);
                Assert.IsTrue(contentStartsBelowTitleBar);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.ForceClose();
                }

                owner.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>验证窗口关闭后迟到的字典类型响应不会写回集合。</summary>
    [TestMethod]
    public async Task Dispose_ShouldIgnoreDictionaryTypesArrivingAfterWindowClose()
    {
        var response = new TaskCompletionSource<ServiceResult<IReadOnlyList<SysDictTypeDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
        {
            nameof(ISysDictTypeApi.GetAllAsync) => response.Task,
            _ => throw new NotSupportedException(method.Name)
        });
        var viewModel = CreateViewModel(typeApi, CreateThrowingProxy<ISysDictDataApi>(), new FakeDialogService(true));

        var initialization = viewModel.InitializeAsync();
        await Task.Yield();
        viewModel.Dispose();
        await initialization;
        response.TrySetResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
        [
            new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "late", DictName = "迟到结果" }
        ]));
        await Task.Yield();

        Assert.AreEqual(0, viewModel.Dictionaries.Count);
    }

    /// <summary>验证未提交值在关闭前触发放弃确认并尊重取消结果。</summary>
    [TestMethod]
    public async Task CanClose_ShouldAskBeforeDiscardingUncommittedDictionaryValues()
    {
        var dialogs = new FakeDialogService(false);
        var viewModel = CreateViewModel(
            CreateThrowingProxy<ISysDictTypeApi>(),
            CreateThrowingProxy<ISysDictDataApi>(),
            dialogs);
        viewModel.Lines.AddNew(new SysDictDataDto { DictCode = "test", DataNameZh = "未保存" });

        var canClose = await viewModel.CanCloseAsync(CancellationToken.None);

        Assert.IsFalse(canClose);
        Assert.AreEqual(1, dialogs.ConfirmCount);
        viewModel.Dispose();
    }

    private static DictionaryManagerViewModel CreateViewModel(
        ISysDictTypeApi typeApi,
        ISysDictDataApi dataApi,
        IDialogService dialogService)
        => new(
            typeApi,
            dataApi,
            new PassThroughApiCallRunner(),
            new NullMessageService(),
            dialogService,
            CreateThrowingProxy<IReferenceDataCacheService>());

    private static T CreateThrowingProxy<T>() where T : class
        => CreateProxy<T>((method, _) => throw new NotSupportedException(method.Name));

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateDispatchProxy>();
        ((DelegateDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
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

    /// <summary>为本测试按方法名提供最小接口替身，避免引入额外 mocking 依赖。</summary>
    public class DelegateDispatchProxy : DispatchProxy
    {
        /// <summary>获取或设置测试接口调用处理器。</summary>
        public Func<MethodInfo, object?[]?, object?> Handler { private get; set; }
            = (_, _) => throw new InvalidOperationException("尚未配置接口替身。");

        /// <summary>把代理调用转发给测试处理器。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod ?? throw new InvalidOperationException("缺少调用方法。"), args);
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

    private sealed class FakeDialogService(bool confirmation) : IDialogService
    {
        public int ConfirmCount { get; private set; }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(confirmation);
        }

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullMessageService : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}

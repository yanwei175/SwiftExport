using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Page.Views;
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
    /// <summary>字典只读会话能打开页面但不能创建类型，赋予独立 Create 后按钮立即可用。</summary>
    [TestMethod]
    public void DictionaryWriteCommands_FollowExactPermissionSnapshot()
    {
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(),
            Permissions = ["System.Dictionary.View"] });
        using var model = new DictionaryManagerViewModel(CreateThrowingProxy<ISysDictTypeApi>(),
            CreateThrowingProxy<ISysDictDataApi>(), new PassThroughApiCallRunner(),
            new NullMessageService(), new FakeDialogService(true),
            CreateThrowingProxy<IReferenceDataCacheService>(), currentUser);
        Assert.IsFalse(model.AddDictionaryTypeCommand.CanExecute(null));

        currentUser.SetCurrentUser(new MeDto { UserId = currentUser.CurrentUser!.UserId,
            Permissions = ["System.Dictionary.View", "System.Dictionary.Create"] });
        Assert.IsTrue(model.AddDictionaryTypeCommand.CanExecute(null));

        currentUser.SetCurrentUser(new MeDto { UserId = currentUser.CurrentUser!.UserId,
            Permissions = ["System.Dictionary.View"] });
        Assert.IsFalse(model.AddDictionaryTypeCommand.CanExecute(null));
    }

    /// <summary>仅有字典读取权时，新系统窗口只载入字典 View，不构造 RBAC 管理页。</summary>
    [TestMethod]
    public void Window_ShouldOpenDictionaryWithoutRbacRequests()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var currentUser = CreateDictionaryUser();
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
            {
                nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(
                    ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([])),
                _ => throw new NotSupportedException(method.Name)
            });
            var viewModel = CreateViewModel(typeApi, CreateThrowingProxy<ISysDictDataApi>(),
                new FakeDialogService(true), currentUser);
            var owner = new FluentWindow
            { Width = 640, Height = 480, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            var window = new SettingsTabTestHost(
                () => throw new AssertFailedException("字典页不应创建 RBAC 页面"),
                () => viewModel,
                () => throw new AssertFailedException("字典页不应创建用户表单"),
                () => throw new AssertFailedException("字典页不应创建重置表单"),
                currentUser, new ContentDialogService(), new WindowMessageService());

            try
            {
                owner.Show();
                window.Owner = owner;
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual("Dictionary", window.CurrentPageName);
                Assert.IsNotNull(FindVisualDescendants<DictionaryManagerView>(window).SingleOrDefault());
                Assert.IsFalse(FindVisualDescendants<TitleBar>(window.View).Any());
                Assert.AreEqual(Visibility.Collapsed,
                    ((FrameworkElement)window.FindName("UsersNavigation")).Visibility);
                window.Close();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(window.IsVisible);
            }
            finally
            {
                if (window.IsVisible) window.ForceClose();
                owner.Close();
            }
        });
    }

    /// <summary>真实导航复用系统设置页签，注销后释放模型并清空页签。</summary>
    [TestMethod]
    public void Navigation_ShouldReuseSettingsTabAndClearOnLogout()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            var currentUser = CreateDictionaryUser();
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
            {
                nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([])),
                _ => throw new NotSupportedException(method.Name)
            });
            var dictionary = CreateViewModel(typeApi, CreateThrowingProxy<ISysDictDataApi>(), new FakeDialogService(false), currentUser);
            using var provider = new ServiceCollection()
                .AddTransient<SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPageViewModel>(_ =>
                    new(() => throw new AssertFailedException("未访问用户页"),
                        () => throw new AssertFailedException(),
                        () => throw new AssertFailedException(), () => dictionary,
                        currentUser, new SettingsDialogStub()))
                .BuildServiceProvider();
            var registry = new SwiftExportClient.Navigation.NavigationRouteRegistry();
            using var navigation = new SwiftExportClient.Navigation.NavigationService(
                new SwiftExportClient.Navigation.NavigationTabFactory(provider.GetRequiredService<IServiceScopeFactory>()),
                registry, new SwiftExportClient.Navigation.NavigationAccessService(registry, currentUser), currentUser,
                new SwiftExportClient.Services.UI.StatusMessageService(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SwiftExportClient.Navigation.NavigationService>.Instance);
            await navigation.OpenAsync("system-settings");
            var first = navigation.SelectedTab;
            Assert.IsNotNull(first);
            await navigation.OpenAsync("system-settings");
            Assert.AreSame(first, navigation.SelectedTab);
            Assert.HasCount(1, navigation.OpenTabs);
            var model = (SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPageViewModel)first.ContentViewModel!;
            await model.InitializeAsync();
            await model.NavigateAsync(SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPage.Dictionary);
            dictionary.Lines.AddNew(new SysDictDataDto { DictCode = "TEST", DataNameZh = "敏感草稿" });
            Assert.IsTrue(first.IsDirty);
            var userId = currentUser.CurrentUser!.UserId;
            currentUser.SetCurrentUser(new MeDto { UserId = userId, Permissions = [] });
            await System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.HasCount(1, navigation.OpenTabs, "全部管理撤权后保留个人设置页签。");
            Assert.IsFalse(model.HasUnsavedChanges, "管理草稿必须清理。");
            Assert.AreEqual(SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPage.Personal, model.CurrentPage);
            currentUser.SetCurrentUser(new MeDto { UserId = userId, Permissions = ["System.Dictionary.View"] });
            await navigation.OpenAsync("system-settings");
            Assert.AreSame(model, navigation.SelectedTab!.ContentViewModel);
            currentUser.Clear();
            Assert.HasCount(0, navigation.OpenTabs);
        });
    }

    /// <summary>字典排序单元格转换失败时提交返回 false，窗口关闭不得越过编辑门禁。</summary>
    [TestMethod]
    public void Window_ShouldStayOpenWhenDictionaryCellCommitFails()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var currentUser = CreateDictionaryUser();
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
            {
                nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(
                    ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
                    [new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "TEST", DictName = "测试字典" }])),
                _ => throw new NotSupportedException(method.Name)
            });
            var dataApi = CreateProxy<ISysDictDataApi>((method, _) => method.Name switch
            {
                nameof(ISysDictDataApi.GetByCodeAsync) => Task.FromResult(
                    ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(
                    [new SysDictDataDto { Uid = Guid.NewGuid(), DictCode = "TEST",
                        DataNameZh = "原值", SortOrder = 1, IsEnabled = true }])),
                _ => throw new NotSupportedException(method.Name)
            });
            var viewModel = CreateViewModel(typeApi, dataApi, new FakeDialogService(true), currentUser);
            var window = new SettingsTabTestHost(
                () => throw new AssertFailedException("字典页不应创建 RBAC 页面"),
                () => viewModel,
                () => throw new AssertFailedException("字典页不应创建用户表单"),
                () => throw new AssertFailedException("字典页不应创建重置表单"),
                currentUser, new ContentDialogService(), new WindowMessageService());
            var owner = new FluentWindow
            { Width = 800, Height = 620, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                owner.Show();
                window.Owner = owner;
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var view = FindVisualDescendants<DictionaryManagerView>(window).Single();
                var grid = (System.Windows.Controls.DataGrid)view.FindName("DictionaryGrid");
                Assert.AreEqual(1, grid.Items.Count);
                Assert.IsFalse(grid.IsReadOnly);
                var item = grid.Items[0];
                grid.ScrollIntoView(item);
                grid.SelectedItem = item;
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(item, grid.Columns[2]);
                Assert.IsTrue(grid.BeginEdit());
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var editor = grid.Columns[2].GetCellContent(item) as System.Windows.Controls.TextBox;
                Assert.IsNotNull(editor);
                editor.Text = "不是数字";
                Assert.IsFalse(view.CommitPendingEdits());
                window.Close();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(window.IsVisible);
            }
            finally
            {
                if (window.IsVisible) window.ForceClose();
                owner.Close();
            }
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

    /// <summary>切离缓存字典页后恢复类型和值快照，返回时不能保存已放弃的编辑。</summary>
    [TestMethod]
    public async Task CanLeavePage_ShouldDiscardConfirmedTypeAndValueDrafts()
    {
        var type = new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "TEST", DictName = "原类型" };
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
        {
            nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(
                ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([type])),
            _ => throw new NotSupportedException(method.Name)
        });
        var dataApi = CreateProxy<ISysDictDataApi>((method, _) => method.Name switch
        {
            nameof(ISysDictDataApi.GetByCodeAsync) => Task.FromResult(
                ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([])),
            _ => throw new NotSupportedException(method.Name)
        });
        var dialogs = new FakeDialogService(true);
        using var viewModel = CreateViewModel(typeApi, dataApi, dialogs);
        await viewModel.InitializeAsync();
        Assert.IsNotNull(viewModel.TypeEditor);
        viewModel.TypeEditor.DictName = "未保存类型";
        viewModel.Lines.AddNew(new SysDictDataDto { DictCode = "TEST", DataNameZh = "未保存值" });

        Assert.IsTrue(viewModel.HasUnsavedChanges);
        Assert.IsTrue(await viewModel.CanLeavePageAsync(CancellationToken.None));
        Assert.AreEqual(1, dialogs.ConfirmCount);
        Assert.IsFalse(viewModel.HasUnsavedChanges);
        Assert.AreEqual("原类型", viewModel.TypeEditor.DictName);
        Assert.HasCount(0, viewModel.Lines.Items);
    }

    /// <summary>在 Shell 实际保活容器中切换页签，不重建设置页面、不丢失字典草稿。</summary>
    [TestMethod]
    public void SettingsTab_ShouldKeepDraftAndViewAcrossShellSwitches()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var currentUser = CreateDictionaryUser();
            var reads = 0;
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) =>
            {
                if (method.Name != nameof(ISysDictTypeApi.GetAllAsync)) throw new NotSupportedException(method.Name);
                reads++;
                return Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([]));
            });
            var dictionary = CreateViewModel(typeApi, CreateThrowingProxy<ISysDictDataApi>(), new FakeDialogService(false), currentUser);
            using var model = new SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPageViewModel(
                () => throw new AssertFailedException(), () => throw new AssertFailedException(),
                () => throw new AssertFailedException(), () => dictionary, currentUser, new SettingsDialogStub());
            var registry = new SwiftExportClient.Navigation.NavigationRouteRegistry();
            Assert.IsTrue(registry.TryGetRoute("system-settings", out var route));
            using var tab = new SwiftExportClient.Navigation.NavigationTabItem(route, model);
            using var other = new SwiftExportClient.Navigation.NavigationTabItem(
                new("other", "其他", "Workspace", typeof(object), "test", null), new object());
            var tabs = new System.Collections.ObjectModel.ObservableCollection<SwiftExportClient.Navigation.NavigationTabItem> { tab, other };
            var host = new SwiftExportClient.Shell.KeepAliveTabContentHost { ItemsSource = tabs, SelectedItem = tab };
            var window = new FluentWindow { Width = 1000, Height = 700, ShowInTaskbar = false, Content = host };
            try
            {
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var view = FindVisualDescendants<SystemSettingsView>(host).Single();
                await model.NavigateAsync(SwiftExportClient.Features.SystemSettings.Page.ViewModels.SettingsPage.Dictionary);
                dictionary.Lines.AddNew(new SysDictDataDto { DictCode = "TEST", DataNameZh = "未保存" });
                Assert.IsTrue(model.HasUnsavedChanges);
                Assert.IsTrue(tab.IsDirty);
                host.SelectedItem = other;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                host.SelectedItem = tab;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreSame(view, FindVisualDescendants<SystemSettingsView>(host).Single());
                Assert.AreEqual(1, reads);
                Assert.IsTrue(model.HasUnsavedChanges);
                Assert.AreEqual("未保存", dictionary.Lines.Items.Single().DataNameZh);
                Assert.IsFalse(await model.CanCloseAsync(CancellationToken.None));
            }
            finally { window.Close(); }
        });
    }

    private static DictionaryManagerViewModel CreateViewModel(
        ISysDictTypeApi typeApi,
        ISysDictDataApi dataApi,
        IDialogService dialogService,
        CurrentUserService? currentUser = null)
        => new(
            typeApi,
            dataApi,
            new PassThroughApiCallRunner(),
            new NullMessageService(),
            dialogService,
            CreateThrowingProxy<IReferenceDataCacheService>(),
            currentUser ?? CreateDictionaryUser());

    /// <summary>界面生命周期测试使用具备全部字典操作的测试会话。</summary>
    private static CurrentUserService CreateDictionaryUser()
    {
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(),
            Permissions = ["System.Dictionary.View", "System.Dictionary.Create", "System.Dictionary.Edit", "System.Dictionary.Delete"] });
        return currentUser;
    }

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

    private sealed class FakeSessionService : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason)
            => SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}

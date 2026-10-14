using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Components;
using SwiftExportClient.Features.SystemSettings.Page.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Mvvm;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>验证字典 ViewModel 作为独立窗口内容时的迟到结果隔离和未保存关闭门禁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DictionaryManagerLifecycleTests
{
    /// <summary>空存储对应的服务端内置类型可维护明细；零版本默认项加载干净，首次保存仍按 Update 提交。</summary>
    [TestMethod]
    public async Task BuiltInDefaults_LoadCleanAndSaveAsZeroVersionUpdate()
    {
        var uid = Guid.NewGuid();
        var type = new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "FlowTestFunction",
            DictName = "服务端功能类型", IsBuiltIn = true, IsSystem = true };
        var data = new SysDictDataDto { Uid = uid, Version = 0, DictCode = type.DictCode,
            DataNameEn = "Default", DataNameZh = "原默认名称", IsBuiltIn = true, IsSystem = true, IsEnabled = true };
        SysDictDataBatchSaveRequest? saved = null;
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
        {
            nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([type])),
            _ => throw new NotSupportedException(method.Name)
        });
        var dataApi = CreateProxy<ISysDictDataApi>((method, args) =>
        {
            if (method.Name == nameof(ISysDictDataApi.GetByCodeAsync))
                return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([data]));
            if (method.Name != nameof(ISysDictDataApi.BatchSaveAsync)) throw new NotSupportedException(method.Name);
            saved = (SysDictDataBatchSaveRequest)args![0]!;
            data.Version = 1; data.DataNameZh = saved.UpdateItems.Single().Data.DataNameZh;
            return Task.FromResult(ServiceResult<SysDictDataBatchSaveResponse>.Ok(new()
            {
                Items = [new SaveItemResult { ClientKey = saved.UpdateItems.Single().ClientKey, Success = true, Uid = uid, OldVersion = 0, NewVersion = 1,
                    Operation = SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Update }]
            }));
        });
        using var model = CreateViewModel(typeApi, dataApi, new FakeDialogService(true),
            cache: new ReferenceDataCacheServiceStub());

        await model.InitializeAsync();
        Assert.HasCount(1, model.Dictionaries, "客户端只能消费服务端清单，不能再补自己的类型列表。");
        Assert.AreEqual(type.DictName, model.SelectedDictionary!.Name);
        Assert.IsTrue(model.CanEditValues);
        Assert.IsTrue(model.IsTypeEditorReadOnly);
        Assert.IsFalse(model.HasUnsavedChanges);
        var line = model.Lines.Items.Single();
        Assert.AreEqual(LineState.Unchanged, line.State);
        Assert.IsFalse(line.IsNew);
        Assert.IsFalse(model.DeleteSelectedCommand.CanExecute(null));
        line.DataNameZh = "显示名称覆盖";
        Assert.AreEqual(LineState.Modified, line.State);
        await model.SaveCommand.ExecuteAsync(null);

        Assert.IsNotNull(saved);
        Assert.HasCount(0, saved.CreateItems);
        Assert.HasCount(1, saved.UpdateItems);
        Assert.AreEqual(uid, saved.UpdateItems[0].Uid);
        Assert.AreEqual(0, saved.UpdateItems[0].Version);
        Assert.AreEqual(1, model.Lines.Items.Single().Version);
        Assert.IsFalse(model.HasUnsavedChanges);
    }

    /// <summary>首次保存版本冲突不刷新掉零版本内置项的用户输入，供用户决定刷新或保留。</summary>
    [TestMethod]
    public async Task BuiltInFirstSaveConflict_KeepsInputAndBaseline()
    {
        var type = new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "FlowTestFunction",
            DictName = "功能", IsBuiltIn = true, IsSystem = true };
        var data = new SysDictDataDto { Uid = Guid.NewGuid(), DictCode = type.DictCode,
            DataNameEn = "Default", DataNameZh = "默认", IsBuiltIn = true, IsSystem = true, IsEnabled = true };
        var reads = 0;
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
        {
            nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([type])),
            _ => throw new NotSupportedException(method.Name)
        });
        var dataApi = CreateProxy<ISysDictDataApi>((method, _) =>
        {
            if (method.Name == nameof(ISysDictDataApi.GetByCodeAsync))
            {
                reads++;
                return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([data]));
            }
            if (method.Name == nameof(ISysDictDataApi.BatchSaveAsync))
                return Task.FromResult(ServiceResult<SysDictDataBatchSaveResponse>.Fail(
                    SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.ConcurrencyConflict, "版本冲突"));
            throw new NotSupportedException(method.Name);
        });
        using var model = CreateViewModel(typeApi, dataApi, new FakeDialogService(true));
        await model.InitializeAsync();
        var line = model.Lines.Items.Single();
        line.DataNameZh = "待保存中文";
        await model.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, reads);
        Assert.AreSame(line, model.Lines.Items.Single());
        Assert.AreEqual("待保存中文", line.DataNameZh);
        Assert.AreEqual(0, line.Version);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.AreEqual("版本冲突", model.ValidationSummary);
    }

    /// <summary>原生表格编辑保护内置代码和启用状态，同时保留显示字段及自定义代码的编辑能力。</summary>
    [TestMethod]
    public void BuiltInGrid_ProtectsOnlyIdentityFields()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
            {
                nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
                    [new() { Uid = Guid.NewGuid(), DictCode = "FlowTestFunction", DictName = "功能", IsBuiltIn = true }])),
                _ => throw new NotSupportedException(method.Name)
            });
            var dataApi = CreateProxy<ISysDictDataApi>((method, _) => method.Name switch
            {
                nameof(ISysDictDataApi.GetByCodeAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(
                    [new() { Uid = Guid.NewGuid(), DictCode = "FlowTestFunction", DataNameEn = "Default", DataNameZh = "默认",
                        IsBuiltIn = true, IsSystem = true, IsEnabled = true },
                     new() { Uid = Guid.NewGuid(), Version = 1, DictCode = "FlowTestFunction", DataNameEn = "Custom", DataNameZh = "自定义" }])),
                _ => throw new NotSupportedException(method.Name)
            });
            using var model = CreateViewModel(typeApi, dataApi, new FakeDialogService(true));
            await model.InitializeAsync();
            var view = new DictionaryManagerView { DataContext = model };
            var window = new Window { Width = 1280, Height = 800, ShowInTaskbar = false, Content = view };
            try
            {
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var grid = (System.Windows.Controls.DataGrid)((DictionaryDataEditor)view.FindName("ValueEditor")).FindName("DictionaryGrid");
                var line = model.Lines.Items[0];
                grid.SelectedItem = line;
                grid.CurrentCell = new DataGridCellInfo(line, grid.Columns[0]);
                Assert.IsFalse(grid.BeginEdit(), "内置英文代码不能通过 F2 或原生编辑入口修改。");
                grid.CurrentCell = new DataGridCellInfo(line, grid.Columns[3]);
                Assert.IsFalse(grid.BeginEdit(), "内置启用状态不能进入编辑。");
                grid.CurrentCell = new DataGridCellInfo(line, grid.Columns[1]);
                Assert.IsTrue(grid.BeginEdit(), "中文显示名应继续允许编辑。");
                grid.CancelEdit(); grid.CancelEdit(DataGridEditingUnit.Row);
                var custom = model.Lines.Items[1];
                grid.SelectedItem = custom;
                grid.CurrentCell = new DataGridCellInfo(custom, grid.Columns[0]);
                Assert.IsTrue(grid.BeginEdit(), "用户自定义英文名称仍可维护。");
                grid.CancelEdit(); grid.CancelEdit(DataGridEditingUnit.Row);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>品质分类接管后，旧类型不应从固定清单或附加类型重现；其他自定义字典仍可维护。</summary>
    [TestMethod]
    public async Task QualityCategoryTree_DoesNotReappearAsGenericDictionary()
    {
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => method.Name switch
        {
            nameof(ISysDictTypeApi.GetAllAsync) => Task.FromResult(
                ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
                [
                    new() { Uid = Guid.NewGuid(), DictCode = "aftersalesqualitycategory   ", DictName = "旧品质分类" },
                    new() { Uid = Guid.NewGuid(), DictCode = "Unit", DictName = "单位" },
                    new() { Uid = Guid.NewGuid(), DictCode = "CustomTest", DictName = "自定义字典" }
                ])),
            _ => throw new NotSupportedException(method.Name)
        });
        var dataApi = CreateProxy<ISysDictDataApi>((method, _) => method.Name switch
        {
            nameof(ISysDictDataApi.GetByCodeAsync) => Task.FromResult(
                ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([])),
            _ => throw new NotSupportedException(method.Name)
        });
        using var model = CreateViewModel(typeApi, dataApi, new FakeDialogService(true));

        await model.InitializeAsync();

        Assert.IsFalse(model.Dictionaries.Any(item => string.Equals(item.Code.TrimEnd(' '),
            "AfterSalesQualityCategory", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(model.Dictionaries.Any(item => item.Code == "Unit"));
        Assert.IsTrue(model.Dictionaries.Any(item => item.Code == "CustomTest"));
    }

    /// <summary>字典只读会话能打开页面但不能创建类型，赋予独立 Create 后按钮立即可用。</summary>
    [TestMethod]
    public void DictionaryWriteCommands_FollowExactPermissionSnapshot()
    {
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString(),
            Permissions = ["System.Dictionary.View"] });
        using var model = CreateViewModel(CreateThrowingProxy<ISysDictTypeApi>(),
            CreateThrowingProxy<ISysDictDataApi>(), new FakeDialogService(true), currentUser);
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
                var grid = (System.Windows.Controls.DataGrid)((DictionaryDataEditor)view.FindName("ValueEditor")).FindName("DictionaryGrid");
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

    /// <summary>独立字典明细页按打开参数读取类型，重复初始化不重载已有草稿。</summary>
    [TestMethod]
    [DataRow("ProductAttributeProfile", "产品属性模板")]
    [DataRow("ProductSeries", "产品系列")]
    public async Task DictionaryDataPage_UsesRequestedCodeAndKeepsDraftOnRepeatedInitialization(string dictCode, string dictionaryName)
    {
        var reads = 0; string? requestedCode = null;
        var api = CreateProxy<ISysDictDataApi>((method, args) =>
        {
            Assert.AreEqual(nameof(ISysDictDataApi.GetByCodeAsync), method.Name);
            reads++; requestedCode = (string)args![0]!;
            return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([]));
        });
        var user = CreateDictionaryUser();
        using var values = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(false), new ReferenceDataCacheServiceStub(), user);
        using var page = new DictionaryDataManagerViewModel(values);
        var request = new DictionaryDataDocumentOpenRequest(dictCode, dictionaryName);
        await page.InitializeDocumentAsync(request, CancellationToken.None);
        values.AddRowCommand.Execute(null); values.SelectedLine!.DataNameZh = "私有字典草稿";
        await page.InitializeDocumentAsync(request, CancellationToken.None);
        Assert.AreEqual(dictCode, requestedCode);
        Assert.AreEqual(dictionaryName, page.DocumentTitle);
        Assert.AreEqual(dictionaryName, values.DictionaryName);
        Assert.AreEqual(1, reads);
        Assert.IsTrue(page.HasUnsavedChanges);
        Assert.AreEqual("私有字典草稿", values.Lines.Items.Single().DataNameZh);
        Assert.IsFalse(await page.CanCloseAsync(default));
    }

    /// <summary>模板和系列明细页分别拥有私有工作区，一个页签撤销不会清除另一个页签的草稿。</summary>
    [TestMethod]
    public async Task DictionaryDataPages_KeepProfileAndSeriesDraftsIndependent()
    {
        var requestedCodes = new List<string>();
        var api = CreateProxy<ISysDictDataApi>((method, args) =>
        {
            Assert.AreEqual(nameof(ISysDictDataApi.GetByCodeAsync), method.Name);
            requestedCodes.Add((string)args![0]!);
            return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([]));
        });
        var user = CreateDictionaryUser();
        using var profileValues = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(true), new ReferenceDataCacheServiceStub(), user);
        using var seriesValues = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(true), new ReferenceDataCacheServiceStub(), user);
        using var profilePage = new DictionaryDataManagerViewModel(profileValues);
        using var seriesPage = new DictionaryDataManagerViewModel(seriesValues);
        var profileRequest = new DictionaryDataDocumentOpenRequest(SwiftExport.Contracts.SysEnums.DictCodes.ProductAttributeProfile, "产品属性模板");
        var seriesRequest = new DictionaryDataDocumentOpenRequest(SwiftExport.Contracts.SysEnums.DictCodes.ProductSeries, "产品系列");
        await profilePage.InitializeDocumentAsync(profileRequest, CancellationToken.None);
        profileValues.AddRowCommand.Execute(null); profileValues.SelectedLine!.DataNameZh = "模板草稿";
        await seriesPage.InitializeDocumentAsync(seriesRequest, CancellationToken.None);
        seriesValues.AddRowCommand.Execute(null); seriesValues.SelectedLine!.DataNameZh = "系列草稿";

        Assert.AreNotEqual(profileRequest.DocumentKey, seriesRequest.DocumentKey);
        Assert.HasCount(2, requestedCodes);
        CollectionAssert.AreEquivalent(new[] { profileRequest.DictCode, seriesRequest.DictCode }, requestedCodes);
        Assert.AreNotSame(profilePage.Values, seriesPage.Values);
        Assert.IsTrue(profilePage.HasUnsavedChanges);
        Assert.IsTrue(seriesPage.HasUnsavedChanges);
        seriesValues.RejectCommand.Execute(null);
        Assert.IsFalse(seriesPage.HasUnsavedChanges);
        Assert.HasCount(0, seriesValues.Lines.Items);
        Assert.IsTrue(profilePage.HasUnsavedChanges);
        Assert.AreEqual("模板草稿", profileValues.Lines.Items.Single().DataNameZh);
    }

    /// <summary>已经确定类型的页签拒绝改用其他类型，不发起读取也不替换当前草稿。</summary>
    [TestMethod]
    public async Task DictionaryDataPage_RejectsDifferentTypeAfterInitialization()
    {
        var reads = 0;
        var api = CreateProxy<ISysDictDataApi>((method, _) =>
        {
            Assert.AreEqual(nameof(ISysDictDataApi.GetByCodeAsync), method.Name); reads++;
            return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([]));
        });
        using var values = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(false), new ReferenceDataCacheServiceStub(), CreateDictionaryUser());
        using var page = new DictionaryDataManagerViewModel(values);
        await page.InitializeDocumentAsync(new DictionaryDataDocumentOpenRequest(SwiftExport.Contracts.SysEnums.DictCodes.ProductSeries, "产品系列"), CancellationToken.None);
        values.AddRowCommand.Execute(null); var draft = values.SelectedLine!; draft.DataNameZh = "系列私有草稿";

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => page.InitializeDocumentAsync(
            new DictionaryDataDocumentOpenRequest(SwiftExport.Contracts.SysEnums.DictCodes.ProductAttributeProfile, "产品属性模板"), CancellationToken.None));

        Assert.AreEqual(1, reads);
        Assert.AreEqual(SwiftExport.Contracts.SysEnums.DictCodes.ProductSeries, values.DictCode);
        Assert.AreEqual("产品系列", page.DocumentTitle);
        Assert.AreSame(draft, values.Lines.Items.Single());
        Assert.AreEqual("系列私有草稿", draft.DataNameZh);
        Assert.IsTrue(page.HasUnsavedChanges);
    }

    /// <summary>新增回执接纳真实身份和版本；缓存失败不会使已提交的模板再次作为新增保存。</summary>
    [TestMethod]
    public async Task ProfileSave_AcceptsReceiptBeforeCacheRefreshFailure()
    {
        var uid = Guid.NewGuid(); var writes = 0;
        var api = CreateProxy<ISysDictDataApi>((method, args) =>
        {
            if (method.Name == nameof(ISysDictDataApi.GetByCodeAsync))
                return Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([]));
            Assert.AreEqual(nameof(ISysDictDataApi.BatchSaveAsync), method.Name); writes++;
            var request = (SysDictDataBatchSaveRequest)args![0]!;
            return Task.FromResult(ServiceResult<SysDictDataBatchSaveResponse>.Ok(new()
            { Items = [new() { ClientKey = request.CreateItems.Single().ClientKey, Success = true,
                Operation = SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Create, Uid = uid, NewVersion = 1 }] }));
        });
        var cache = CreateProxy<IReferenceDataCacheService>((method, _) =>
        {
            Assert.AreEqual(nameof(IReferenceDataCacheService.RefreshDictionariesAsync), method.Name);
            return Task.FromResult(CacheRefreshResult.Fail("离线"));
        });
        using var values = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(true), cache, CreateDictionaryUser()) { ContextEditable = true };
        await values.ActivateAsync("ProductAttributeProfile", "属性模板"); values.AddRowCommand.Execute(null);
        var line = values.SelectedLine!; line.DataNameEn = "Tap"; line.DataNameZh = "龙头";
        Assert.IsTrue(await values.TrySaveAsync());
        Assert.AreEqual(uid, line.Uid); Assert.AreEqual(1, line.Version);
        Assert.IsFalse(values.HasUnsavedChanges);
        Assert.IsFalse(await values.TrySaveAsync()); Assert.AreEqual(1, writes);
    }

    /// <summary>不完整成功回执保留草稿并禁止重复提交，必须显式刷新核对。</summary>
    [TestMethod]
    public async Task ProfileSave_IncompleteReceiptKeepsDraftAndRequiresReload()
    {
        var api = CreateProxy<ISysDictDataApi>((method, _) => method.Name switch
        {
            nameof(ISysDictDataApi.GetByCodeAsync) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([])),
            nameof(ISysDictDataApi.BatchSaveAsync) => Task.FromResult(ServiceResult<SysDictDataBatchSaveResponse>.Ok(new())),
            _ => throw new AssertFailedException(method.Name)
        });
        using var values = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(true), new ReferenceDataCacheServiceStub(), CreateDictionaryUser()) { ContextEditable = true };
        await values.ActivateAsync("ProductAttributeProfile", "模板"); values.AddRowCommand.Execute(null);
        values.SelectedLine!.DataNameEn = "Tap"; values.SelectedLine.DataNameZh = "龙头";
        Assert.IsFalse(await values.TrySaveAsync()); Assert.IsTrue(values.RequiresReload);
        Assert.IsTrue(values.HasUnsavedChanges); Assert.AreEqual("龙头", values.Lines.Items.Single().DataNameZh);
        Assert.IsFalse(values.SaveCommand.CanExecute(null));
    }

    /// <summary>字典切换尊重子工作区草稿取消和原生编辑转换失败，不替换当前明细。</summary>
    [TestMethod]
    public async Task DictionarySwitch_ProtectsExtractedValueDraftAndNativeEdit()
    {
        var typeApi = CreateProxy<ISysDictTypeApi>((method, _) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
            [new() { Uid = Guid.NewGuid(), DictCode = "A", DictName = "A" }, new() { Uid = Guid.NewGuid(), DictCode = "B", DictName = "B" }])));
        var dataApi = CreateProxy<ISysDictDataApi>((method, _) => Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([])));
        using var model = CreateViewModel(typeApi, dataApi, new FakeDialogService(false)); await model.InitializeAsync();
        model.AddRowCommand.Execute(null); model.SelectedLine!.DataNameZh = "保留草稿";
        var selected = model.SelectedDictionary; var target = model.Dictionaries.Single(item => item != selected);
        await model.SelectDictionaryCommand.ExecuteAsync(target);
        Assert.AreSame(selected, model.SelectedDictionary); Assert.AreEqual("保留草稿", model.Lines.Items.Single().DataNameZh);
        model.Values.CommitPendingEdits = () => false;
        Assert.IsFalse(await model.CanCloseAsync(default));
        await model.SelectDictionaryCommand.ExecuteAsync(target);
        Assert.AreSame(selected, model.SelectedDictionary);
    }

    /// <summary>固定类型查询在注销后不得回写，即使随后同一用户重新登录。</summary>
    [TestMethod]
    public async Task ProfileRead_RejectsLateOldSessionResponse()
    {
        var response = new TaskCompletionSource<ServiceResult<IReadOnlyList<SysDictDataDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = CreateProxy<ISysDictDataApi>((method, _) => response.Task);
        var user = CreateDictionaryUser(); var id = user.CurrentUser!.UserId;
        using var values = new DictionaryDataEditorViewModel(api, new PassThroughApiCallRunner(), new NullMessageService(),
            new FakeDialogService(true), new ReferenceDataCacheServiceStub(), user) { ContextEditable = true };
        var loading = values.ActivateAsync("ProductAttributeProfile", "模板");
        user.Clear(); user.SetCurrentUser(new() { UserId = id, Permissions = ["System.Dictionary.View", "System.Dictionary.Edit"] });
        await loading;
        response.SetResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([new() { Uid = Guid.NewGuid(), DataNameZh = "迟到" }]));
        await Task.Yield();
        Assert.HasCount(0, values.Lines.Items); Assert.IsFalse(values.IsLoaded); Assert.IsTrue(values.IsValueEditorReadOnly);
    }

    private static DictionaryManagerViewModel CreateViewModel(
        ISysDictTypeApi typeApi,
        ISysDictDataApi dataApi,
        IDialogService dialogService,
        CurrentUserService? currentUser = null,
        IReferenceDataCacheService? cache = null)
    {
        currentUser ??= CreateDictionaryUser();
        cache ??= CreateThrowingProxy<IReferenceDataCacheService>();
        var runner = new PassThroughApiCallRunner();
        var messages = new NullMessageService();
        var values = new DictionaryDataEditorViewModel(dataApi, runner, messages, dialogService, cache, currentUser);
        return new(typeApi, values, runner, messages, dialogService, cache, currentUser);
    }

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

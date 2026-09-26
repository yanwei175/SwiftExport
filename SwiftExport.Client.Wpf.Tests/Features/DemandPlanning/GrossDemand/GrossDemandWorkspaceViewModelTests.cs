using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed class GrossDemandWorkspaceViewModelTests
{
    [TestMethod]
    public async Task CurrentPageSelection_ShouldMaintainTriStateAndCommandCapability()
    {
        var api = new FakeGrossDemandApi
        {
            SearchResponse = CreatePage(
                CreateItem("SO-1", canDecompose: true),
                CreateItem("SO-2", canDecompose: false)),
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();

        viewModel.Items[0].IsChecked = true;
        Assert.IsNull(viewModel.IsAllChecked);

        viewModel.Items[1].IsChecked = true;
        Assert.AreEqual(true, viewModel.IsAllChecked);

        viewModel.IsAllChecked = false;
        Assert.IsTrue(viewModel.Items.All(item => !item.IsChecked));
        Assert.AreEqual(0, viewModel.CheckedCount);
    }

    [TestMethod]
    public async Task Search_ShouldSplitMultilineConditionsAndResetPage()
    {
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage() };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.PageIndex = 3;
        viewModel.SourceNosText = "SO-1\nSO-2,SO-1";
        viewModel.SkusText = "A-1；A-2";
        viewModel.CustomerSkusText = "C-1;C-2";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        Assert.AreEqual(1, api.LastSearchRequest.PageIndex);
        CollectionAssert.AreEquivalent(new[] { "SO-1", "SO-2" }, api.LastSearchRequest.SourceNos);
        CollectionAssert.AreEquivalent(new[] { "A-1", "A-2" }, api.LastSearchRequest.Skus);
        CollectionAssert.AreEquivalent(new[] { "C-1", "C-2" }, api.LastSearchRequest.CustomerSkus);
    }

    [TestMethod]
    public async Task SelectingSourceGroup_ShouldLoadWholeSourceWithoutChangingNormalPageSize()
    {
        var sourceUid = Guid.NewGuid();
        var items = Enumerable.Range(1, 250)
            .Select(index => CreateItem($"SO-WHOLE-{index}", canDecompose: true))
            .ToArray();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-WHOLE",
                    DemandCount = items.Length,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        api.SearchResponse = new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = items.ToList(),
            TotalCount = items.Length,
            PageIndex = 1,
            PageSize = items.Length,
        };

        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);

        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(sourceUid, api.LastSearchRequest?.SourceUid);
        Assert.AreEqual(250, viewModel.Items.Count);
        Assert.AreEqual(20, viewModel.PageSize);
        Assert.AreEqual(1, viewModel.TotalPages);
        Assert.IsFalse(viewModel.PageChangeCommand.CanExecute(null));
        StringAssert.Contains(viewModel.ResultSummary, "已全部加载");
        Assert.AreEqual(1, api.SourceGroupCallCount);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            api.LastSourceGroupRequest?.LifecycleStatuses);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            api.LastSearchRequest?.LifecycleStatuses);
        Assert.IsTrue(string.IsNullOrWhiteSpace(api.LastSearchRequest?.Keyword));
    }

    [TestMethod]
    public async Task Refresh_WhenSelectedSourceNoLongerExists_ShouldRestoreMainSearch()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-REMOVED",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        api.SourceGroupResponse = [];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsTrue(viewModel.IsSourcePanelOpen);
    }

    [TestMethod]
    public async Task SourcePanel_ShouldUseIndependentDefaultsAndOpeningMustNotReloadGrid()
    {
        var initialItem = CreateItem("SO-INITIAL", canDecompose: true);
        var api = new FakeGrossDemandApi
        {
            SearchResponse = CreatePage(initialItem),
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = initialItem.SourceUid,
                    SourceType = "SO",
                    SourceNo = initialItem.SourceNo,
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.IsSourcePanelOpen);
        Assert.AreEqual(0, api.SourceGroupCallCount);
        Assert.AreEqual(1, api.SearchCallCount);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            viewModel.SourceLifecycleStatuses.Select(item => item.Value).ToArray());

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.AreEqual(1, api.SourceGroupCallCount);
        Assert.AreEqual(1, api.SearchCallCount);
        Assert.AreEqual(initialItem.Uid, viewModel.Items.Single().Uid);
    }

    [TestMethod]
    public async Task ClosingSourcePanel_ShouldClearSelectionAndRestoreMainPagedSearch()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-CLOSE",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        var callsBeforeClosing = api.SearchCallCount;

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourcePanelOpen);
        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.AreEqual(callsBeforeClosing + 1, api.SearchCallCount);
    }

    [TestMethod]
    public async Task ClearSourceFilters_WithoutSelection_ShouldOnlyRefreshSourceList()
    {
        var initialItem = CreateItem("SO-MAIN", canDecompose: true);
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(initialItem) };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        var searchCallsBeforeClear = api.SearchCallCount;

        await viewModel.ClearSourceFiltersCommand.ExecuteAsync(null);

        Assert.AreEqual(0, viewModel.SourceLifecycleStatuses.Count);
        Assert.AreEqual(searchCallsBeforeClear, api.SearchCallCount);
        Assert.AreEqual(initialItem.Uid, viewModel.Items.Single().Uid);
        Assert.AreEqual(2, api.SourceGroupCallCount);
        Assert.AreEqual(0, api.LastSourceGroupRequest?.LifecycleStatuses.Count);
    }

    [TestMethod]
    public async Task MainSearch_ShouldExitSourceModeButKeepPanelOpen()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-MAIN-SEARCH",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        viewModel.Keyword = "主区域条件";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.AreEqual("主区域条件", api.LastSearchRequest?.Keyword);
    }

    [TestMethod]
    public async Task ClosingSourcePanel_WhenDirtyDiscardRejected_ShouldKeepSourceMode()
    {
        var sourceUid = Guid.NewGuid();
        var dialog = new RecordingDialogService(confirmResult: false);
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-DIRTY",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api, dialog);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "未保存";

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(sourceUid, viewModel.SelectedSourceGroup?.SourceUid);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsClean_ShouldCloseWithoutConfirmation()
    {
        var dialog = new RecordingDialogService(confirmResult: false);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsFalse(viewModel.ManualEditor.IsOpen);
        Assert.AreEqual(0, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsDirtyAndDiscardRejected_ShouldKeepEditorOpen()
    {
        var dialog = new RecordingDialogService(confirmResult: false);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "尚未保存";

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsDrawerOpen);
        Assert.IsTrue(viewModel.ManualEditor.IsOpen);
        Assert.IsTrue(viewModel.ManualEditor.HasUnsavedChanges);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsDirtyAndDiscardConfirmed_ShouldDiscardAndClose()
    {
        var dialog = new RecordingDialogService(confirmResult: true);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "尚未保存";

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsFalse(viewModel.ManualEditor.IsOpen);
        Assert.IsFalse(viewModel.ManualEditor.HasUnsavedChanges);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public void ManualEditorCancelButton_ShouldBindWorkspaceCloseDrawerCommand()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandManualEditorView.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cancelButton = document
            .Descendants(presentation + "Button")
            .Single(element => string.Equals((string?)element.Attribute(x + "Name"), "CancelButton", StringComparison.Ordinal));

        Assert.AreEqual("取消", (string?)cancelButton.Attribute("Content"));
        StringAssert.Contains((string?)cancelButton.Attribute("Command") ?? string.Empty, "DataContext.CloseDrawerCommand");
        StringAssert.Contains((string?)cancelButton.Attribute("Command") ?? string.Empty, "GrossDemandWorkspaceView");
    }

    [TestMethod]
    public void Workspace_ShouldNotLoadDownstreamTraceFeature()
    {
        var repositoryRoot = FindRepositoryRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        Assert.IsFalse(workspace.Descendants(presentation + "ToggleButton")
            .Any(element => string.Equals((string?)element.Attribute("Content"), "拆解结果", StringComparison.Ordinal)));
        Assert.IsFalse(workspace.Descendants()
            .Any(element => ((string?)element.Attribute("DataType"))?.Contains("GrossDemandTraceViewModel", StringComparison.Ordinal) == true));
        Assert.IsFalse(workspace.Descendants()
            .Any(element => ((string?)element.Attribute("Placeholder"))?.Contains("执行进度", StringComparison.Ordinal) == true));
    }

    /// <summary>
    /// 验证多行筛选通过“更多条件”按钮控制全宽弹出层，不回退为折叠面板。
    /// </summary>
    [TestMethod]
    public void MultiLineFilters_ShouldUseToggleControlledFullWidthPopup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.IsFalse(workspace.Descendants(presentation + "Expander")
            .Any(element => (string?)element.Attribute("Header") is "多行条件" or "更多条件"));
        var toggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => ((string?)element.Attribute("IsChecked"))
                ?.Contains("IsMultiLineFilterOpen", StringComparison.Ordinal) == true);
        Assert.AreEqual("更多条件", (string?)toggle.Attribute("Content"));
        StringAssert.Contains((string?)toggle.Attribute("IsChecked") ?? string.Empty, "IsMultiLineFilterOpen");

        var popup = workspace.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsMultiLineFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("PlacementTarget") ?? string.Empty, "StackPanel");
        StringAssert.Contains((string?)popup.Attribute("Width") ?? string.Empty, "PlacementTarget.ActualWidth");
        Assert.AreEqual("True", (string?)popup.Attribute("StaysOpen"));
        Assert.AreEqual(3, popup.Descendants().Count(element => element.Name.LocalName == "TextBox"));
    }

    /// <summary>
    /// 验证来源分组区使用独立筛选和整高列表，并在来源模式下隐藏分页区域。
    /// </summary>
    [TestMethod]
    public void SourceGroups_ShouldUseFullHeightListAndHidePagerInSourceMode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var sourceList = workspace.Descendants(presentation + "ListBox").Single();
        StringAssert.Contains((string?)sourceList.Attribute("ItemsSource") ?? string.Empty, "SourceGroups");
        StringAssert.Contains((string?)sourceList.Attribute("SelectedItem") ?? string.Empty, "SelectedSourceGroup");
        Assert.AreEqual("OnSourceGroupSelectionChanged", (string?)sourceList.Attribute("SelectionChanged"));

        var sourcePanel = sourceList.Ancestors(presentation + "Border").First();
        Assert.AreEqual("180", (string?)sourcePanel.Attribute("Width"));
        StringAssert.Contains((string?)sourcePanel.Attribute("Visibility") ?? string.Empty, "IsSourcePanelOpen");
        Assert.AreEqual("Left", (string?)sourcePanel.Attribute("DockPanel.Dock"));

        var sourceToggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => ((string?)element.Attribute("Command"))
                ?.Contains("ToggleSourcePanelCommand", StringComparison.Ordinal) == true);
        Assert.AreEqual("来源单号分组", (string?)sourceToggle.Attribute("Content"));
        StringAssert.Contains((string?)sourceToggle.Attribute("Command") ?? string.Empty, "ToggleSourcePanelCommand");
        StringAssert.Contains((string?)sourceToggle.Attribute("IsChecked") ?? string.Empty, "IsSourcePanelOpen");
        var multilineToggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => ((string?)element.Attribute("IsChecked"))
                ?.Contains("IsMultiLineFilterOpen", StringComparison.Ordinal) == true);
        Assert.AreEqual("更多条件", (string?)multilineToggle.Attribute("Content"));
        Assert.IsTrue(GetGridColumn(sourceToggle) < GetGridColumn(multilineToggle));

        var sourceFilters = sourcePanel.Descendants().Where(element => element.Name.LocalName == "DropDownCheckBox").ToArray();
        Assert.AreEqual(1, sourceFilters.Length);
        Assert.IsTrue(sourceFilters.Any(element => ((string?)element.Attribute("SelectedItems"))?.Contains("SourceLifecycleStatuses", StringComparison.Ordinal) == true));
        Assert.IsFalse(sourceFilters.Any(element => ((string?)element.Attribute("SelectedItems"))?.Contains("SourceDecompositionStatuses", StringComparison.Ordinal) == true));
        Assert.IsTrue(sourcePanel.Descendants(presentation + "Button")
            .Any(element => string.Equals((string?)element.Attribute("Content"), "清空筛选", StringComparison.Ordinal)));
        Assert.IsFalse(sourcePanel.Descendants().Any(element => string.Equals((string?)element.Attribute("Content"), "全部", StringComparison.Ordinal)));

        var sourceModeTrigger = workspace.Descendants(presentation + "DataTrigger")
            .Single(element => ((string?)element.Attribute("Binding"))?.Contains("IsSourceSelected", StringComparison.Ordinal) == true);
        Assert.AreEqual("True", (string?)sourceModeTrigger.Attribute("Value"));
    }

    [TestMethod]
    public void WorkspaceViewModel_ShouldKeepRequestedChineseRegions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "ViewModels",
            "GrossDemandWorkspaceViewModel.cs"));

        foreach (var region in new[]
                 {
                     "来源导航与独立筛选",
                     "主区域筛选与分页",
                     "毛需求业务命令",
                     "抽屉与来源追溯",
                     "查询加载与并发控制",
                     "选择状态与辅助方法",
                 })
        {
            StringAssert.Contains(source, $"#region {region}");
        }
    }

    private static int GetGridColumn(XElement element) =>
        int.TryParse((string?)element.Attribute("Grid.Column"), out var column)
            ? column
            : 0;

    private static GrossDemandWorkspaceViewModel CreateViewModel(
        FakeGrossDemandApi api,
        IDialogService? dialogService = null)
    {
        var runner = new PassThroughApiCallRunner();
        var messages = new TestMessageService();
        var manual = new GrossDemandManualEditorViewModel(
            api,
            DispatchProxy.Create<IProductApi, NoCallProxy>(),
            runner,
            messages);
        var navigation = DispatchProxy.Create<INavigationService, NoCallProxy>();
        return new GrossDemandWorkspaceViewModel(
            api,
            runner,
            navigation,
            dialogService ?? new RecordingDialogService(confirmResult: true),
            messages,
            manual);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    private static GrossDemandListItemDto CreateItem(string sourceNo, bool canDecompose) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
        SourceType = "SO",
        SourceNo = sourceNo,
        SourceUid = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        Sku = sourceNo,
        Description = sourceNo,
        DemandQty = 1m,
    };

    private static PagedResponseBase<GrossDemandListItemDto> CreatePage(params GrossDemandListItemDto[] items) => new()
    {
        Items = items.ToList(),
        TotalCount = items.Length,
        PageIndex = 1,
        PageSize = 20,
    };

    private sealed class FakeGrossDemandApi : IGrossDemandWorkspaceApi
    {
        public PagedResponseBase<GrossDemandListItemDto> SearchResponse { get; set; } = CreatePage();
        public List<GrossDemandSourceGroupDto> SourceGroupResponse { get; set; } = [];
        public GrossDemandSearchRequest? LastSearchRequest { get; private set; }
        public GrossDemandSourceGroupSearchRequest? LastSourceGroupRequest { get; private set; }
        public int SearchCallCount { get; private set; }
        public int SourceGroupCallCount { get; private set; }
        public int TraceCallCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            SearchCallCount++;
            return Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(SearchResponse));
        }

        public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            GrossDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSourceGroupRequest = request;
            SourceGroupCallCount++;
            return Task.FromResult(ServiceResult<List<GrossDemandSourceGroupDto>>.Ok(SourceGroupResponse));
        }

        public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command) => throw new NotSupportedException();
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    public class NoCallProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class RecordingDialogService(bool confirmResult) : IDialogService
    {
        public int ConfirmCount { get; private set; }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(confirmResult);
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestMessageService : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}

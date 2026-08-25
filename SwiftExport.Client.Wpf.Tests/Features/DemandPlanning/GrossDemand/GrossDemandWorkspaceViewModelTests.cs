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
        Assert.IsTrue(viewModel.DecomposeSelectedCommand.CanExecute(null));

        viewModel.Items[1].IsChecked = true;
        Assert.AreEqual(true, viewModel.IsAllChecked);
        Assert.IsFalse(viewModel.DecomposeSelectedCommand.CanExecute(null));

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
        api.SearchResponse = new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = items.ToList(),
            TotalCount = items.Length,
            PageIndex = 1,
            PageSize = items.Length,
        };

        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);

        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(sourceUid, api.LastSearchRequest?.SourceUid);
        Assert.AreEqual(250, viewModel.Items.Count);
        Assert.AreEqual(20, viewModel.PageSize);
        Assert.AreEqual(1, viewModel.TotalPages);
        Assert.IsFalse(viewModel.PageChangeCommand.CanExecute(null));
        StringAssert.Contains(viewModel.ResultSummary, "已全部加载");
        Assert.AreEqual(1, api.SourceGroupCallCount);
    }

    [TestMethod]
    public async Task Refresh_WhenSelectedSourceNoLongerExists_ShouldReturnToAll()
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
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
        api.SourceGroupResponse = [];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.AreEqual("全部", viewModel.SelectedSourceGroup?.DisplaySourceNo);
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
    public async Task TraceToggle_ShouldOpenAndCloseTraceDrawer()
    {
        var item = CreateItem("SO-TRACE", canDecompose: false);
        var api = new FakeGrossDemandApi
        {
            SearchResponse = CreatePage(item),
            TraceResponse = new GrossDemandTraceDto
            {
                GrossDemand = new GrossDemandDetailDto { Uid = item.Uid }
            }
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();

        await viewModel.ToggleTraceCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsDrawerOpen);
        Assert.IsTrue(viewModel.IsTraceOpen);
        Assert.AreSame(viewModel.Trace, viewModel.ActiveDrawerContent);
        Assert.AreEqual(1, api.TraceCallCount);

        await viewModel.ToggleTraceCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsFalse(viewModel.IsTraceOpen);
        Assert.IsNull(viewModel.ActiveDrawerContent);
        Assert.AreEqual(1, api.TraceCallCount);
    }

    [TestMethod]
    public void TraceViews_ShouldBindToggleAndCloseToWorkspaceCommands()
    {
        var repositoryRoot = FindRepositoryRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        var toggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => string.Equals(
                (string?)element.Attribute("Content"),
                "拆解结果",
                StringComparison.Ordinal));
        StringAssert.Contains((string?)toggle.Attribute("Command") ?? string.Empty, "ToggleTraceCommand");
        StringAssert.Contains((string?)toggle.Attribute("IsChecked") ?? string.Empty, "IsTraceOpen");

        var trace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandTraceView.xaml"));
        var close = trace.Descendants(presentation + "Button")
            .Single(element => string.Equals(
                (string?)element.Attribute(x + "Name"),
                "CloseButton",
                StringComparison.Ordinal));
        Assert.AreEqual("关闭", (string?)close.Attribute("Content"));
        StringAssert.Contains((string?)close.Attribute("Command") ?? string.Empty, "DataContext.CloseDrawerCommand");
        StringAssert.Contains((string?)close.Attribute("Command") ?? string.Empty, "GrossDemandWorkspaceView");
    }

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
            .Any(element => string.Equals((string?)element.Attribute("Header"), "多行条件", StringComparison.Ordinal)));
        var toggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => string.Equals((string?)element.Attribute("Content"), "多行条件", StringComparison.Ordinal));
        StringAssert.Contains((string?)toggle.Attribute("IsChecked") ?? string.Empty, "IsMultiLineFilterOpen");

        var popup = workspace.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsMultiLineFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("PlacementTarget") ?? string.Empty, "StackPanel");
        StringAssert.Contains((string?)popup.Attribute("Width") ?? string.Empty, "PlacementTarget.ActualWidth");
        Assert.AreEqual("True", (string?)popup.Attribute("StaysOpen"));
        Assert.AreEqual(3, popup.Descendants().Count(element => element.Name.LocalName == "TextBox"));
    }

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

        var outerGrid = sourceList.Ancestors(presentation + "Grid")
            .Last(element => ((string?)element.Attribute("ColumnDefinitions"))?.Contains("180,8,*", StringComparison.Ordinal) == true);
        Assert.IsNotNull(outerGrid);
        var sourceModeTrigger = workspace.Descendants(presentation + "DataTrigger")
            .Single(element => ((string?)element.Attribute("Binding"))?.Contains("IsSourceSelected", StringComparison.Ordinal) == true);
        Assert.AreEqual("True", (string?)sourceModeTrigger.Attribute("Value"));
    }

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
        var trace = new GrossDemandTraceViewModel(api, runner, messages, navigation);
        return new GrossDemandWorkspaceViewModel(
            api,
            runner,
            navigation,
            dialogService ?? new RecordingDialogService(confirmResult: true),
            messages,
            manual,
            trace);
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
        DecompositionStatus = GrossDemandDecompositionStatusEnum.Pending,
        SourceType = "SO",
        SourceNo = sourceNo,
        SourceUid = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        Sku = sourceNo,
        Description = sourceNo,
        DemandQty = 1m,
        CanDecompose = canDecompose,
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
        public GrossDemandTraceDto TraceResponse { get; set; } = new()
        {
            GrossDemand = new GrossDemandDetailDto()
        };
        public GrossDemandSearchRequest? LastSearchRequest { get; private set; }
        public GrossDemandSearchRequest? LastSourceGroupRequest { get; private set; }
        public int SourceGroupCallCount { get; private set; }
        public int TraceCallCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(SearchResponse));
        }

        public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            GrossDemandSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSourceGroupRequest = request;
            SourceGroupCallCount++;
            return Task.FromResult(ServiceResult<List<GrossDemandSourceGroupDto>>.Ok(SourceGroupResponse));
        }

        public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
        {
            TraceCallCount++;
            return Task.FromResult(ServiceResult<GrossDemandTraceDto>.Ok(TraceResponse));
        }
        public Task<ServiceResult<GrossDemandDecomposeResult>> DecomposeAsync(GrossDemandDecomposeCommand command) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandRollbackResult>> RollbackAsync(GrossDemandRollbackCommand command) => throw new NotSupportedException();
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

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
        public GrossDemandSearchRequest? LastSearchRequest { get; private set; }

        public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(SearchResponse));
        }

        public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<GrossDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

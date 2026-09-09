using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>
/// 验证净需求来源导航与更多条件 Popup 的关键交互契约。
/// </summary>
[TestClass]
public sealed class NetDemandWorkspaceSourceNavigationTests
{
    [TestMethod]
    public async Task SelectingSource_ShouldUseSourceFactFilterAndKeepNormalPageSize()
    {
        var sourceUid = Guid.NewGuid();
        var api = new RecordingNetDemandApi(sourceUid);
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.Keyword = "顶部条件不应进入来源模式";
        viewModel.SourceSourceTypes.Add(viewModel.SourceTypeOptions[0]);

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.AreEqual(1, api.SourceGroupRequests.Count);
        CollectionAssert.AreEqual(new[] { "SO" }, api.SourceGroupRequests[0].SourceTypes);

        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups.Single());

        var sourceRequest = api.SearchRequests.Last();
        Assert.AreEqual(sourceUid, sourceRequest.SourceUid);
        CollectionAssert.AreEqual(new[] { "SO" }, sourceRequest.SourceTypes);
        Assert.IsTrue(string.IsNullOrWhiteSpace(sourceRequest.Keyword));
        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(20, viewModel.PageSize);
        Assert.AreEqual(1, viewModel.TotalPages);
        Assert.AreEqual("当前来源共 2 条", viewModel.PageStatus);
        CollectionAssert.Contains(changedProperties, nameof(NetDemandWorkspaceViewModel.IsSourceSelected));
        CollectionAssert.Contains(changedProperties, nameof(NetDemandWorkspaceViewModel.PageStatus));
    }

    [TestMethod]
    public async Task MainSearch_ShouldExitSourceModeAndRestorePagingQuery()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups.Single());
        viewModel.Keyword = "SO-SEARCH";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.IsNull(api.SearchRequests.Last().SourceUid);
        Assert.AreEqual("SO-SEARCH", api.SearchRequests.Last().Keyword);
    }

    [TestMethod]
    public async Task SafeRollback_ShouldKeepSourceRevisionCommandAvailable()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.Items.Single().IsChecked = true;
        var grossDemandId = viewModel.Items.Single().GrossDemandId;

        await viewModel.RollbackSourceEstablishmentCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.RollbackCommands.Count);
        Assert.AreEqual(grossDemandId, api.RollbackCommands[0].Items.Single().GrossDemandId);
    }

    [TestMethod]
    public void WorkspaceView_ShouldExposeSourceListAndFullWidthMultilinePopup()
    {
        var workspace = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "NetDemand",
            "Views",
            "NetDemandWorkspaceView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var sourceList = workspace.Descendants(presentation + "ListBox").Single();
        StringAssert.Contains((string?)sourceList.Attribute("ItemsSource") ?? string.Empty, "SourceGroups");
        StringAssert.Contains((string?)sourceList.Attribute("SelectedItem") ?? string.Empty, "SelectedSourceGroup");
        Assert.AreEqual("OnSourceGroupSelectionChanged", (string?)sourceList.Attribute("SelectionChanged"));

        var sourcePanel = sourceList.Ancestors(presentation + "Border").First();
        Assert.AreEqual(1, sourcePanel.Descendants().Count(element => element.Name.LocalName == "DropDownCheckBox"));
        StringAssert.Contains((string?)sourcePanel.Attribute("Visibility") ?? string.Empty, "IsSourcePanelOpen");

        var popup = workspace.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsMultiLineFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("Width") ?? string.Empty, "PlacementTarget.ActualWidth");
        var textBoxes = popup.Descendants().Where(element => element.Name.LocalName == "TextBox").ToArray();
        Assert.AreEqual(3, textBoxes.Length);
        Assert.IsTrue(textBoxes.All(element => string.Equals((string?)element.Attribute("AcceptsReturn"), "True", StringComparison.Ordinal)));
        Assert.IsTrue(textBoxes.All(element => string.Equals((string?)element.Attribute("TextWrapping"), "Wrap", StringComparison.Ordinal)));
        Assert.AreEqual(2, popup.Descendants(presentation + "DatePicker").Count());

        var buttonTexts = workspace.Descendants(presentation + "Button")
            .Select(element => (string?)element.Attribute("Content"))
            .Where(text => text is not null)
            .ToArray();
        CollectionAssert.DoesNotContain(buttonTexts, "修改计划");
        CollectionAssert.DoesNotContain(buttonTexts, "批量分配供应商");
        CollectionAssert.DoesNotContain(buttonTexts, "应用产品默认供应商");
        CollectionAssert.DoesNotContain(buttonTexts, "人工转采购");
        CollectionAssert.DoesNotContain(buttonTexts, "来源一键转采购");
        CollectionAssert.DoesNotContain(buttonTexts, "计划追溯");

        var pagerTrigger = workspace.Descendants(presentation + "DataTrigger")
            .Single(element => ((string?)element.Attribute("Binding"))?.Contains("IsSourceSelected", StringComparison.Ordinal) == true);
        Assert.AreEqual("True", (string?)pagerTrigger.Attribute("Value"));
    }

    private static NetDemandWorkspaceViewModel CreateViewModel(RecordingNetDemandApi api)
    {
        var runner = new DirectApiCallRunner();
        var messages = new MessageServiceStub();
        var dialogs = new DialogServiceStub();
        return new NetDemandWorkspaceViewModel(
            api,
            runner,
            dialogs,
            messages,
            DispatchProxy.Create<INetDemandDialogService, NoCallProxy>());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    private sealed class RecordingNetDemandApi(Guid sourceUid) : IMaterialDemandWorkspaceApi
    {
        public List<MaterialDemandWorkspaceSearchRequest> SearchRequests { get; } = [];
        public List<MaterialDemandSourceGroupSearchRequest> SourceGroupRequests { get; } = [];
        public List<RollbackMaterialDemandsCommand> RollbackCommands { get; } = [];

        public Task<ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>> SearchAsync(
            MaterialDemandWorkspaceSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchRequests.Add(request);
            var sourceMode = request.SourceUid.HasValue;
            var items = sourceMode
                ? new[] { CreateItem(sourceUid, 1), CreateItem(sourceUid, 2) }
                : new[] { CreateItem(sourceUid, 1) };
            return Task.FromResult(ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>.Ok(new PagedResponseBase<MaterialDemandWorkspaceItemDto>
            {
                Items = items.ToList(),
                TotalCount = items.Length,
                PageIndex = 1,
                PageSize = sourceMode ? items.Length : 20,
            }));
        }

        public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            MaterialDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SourceGroupRequests.Add(request);
            return Task.FromResult(ServiceResult<List<NetDemandSourceGroupDto>>.Ok(
            [
                new NetDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-1001",
                    DemandCount = 2,
                },
            ]));
        }

        public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default)
        {
            RollbackCommands.Add(command);
            return Task.FromResult(ServiceResult<RollbackMaterialDemandsResult>.Ok(new RollbackMaterialDemandsResult
            {
                RolledBackSourceCount = command.Items.Count,
            }));
        }

        private static MaterialDemandWorkspaceItemDto CreateItem(Guid sourceUid, int lineNumber) => new()
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            GrossDemandId = Guid.NewGuid(),
            LineNumber = lineNumber,
            SourceType = "SO",
            SourceUid = sourceUid,
            SourceNo = "SO-1001",
            RootNetDemandId = Guid.NewGuid(),
            Sku = $"SKU-{lineNumber}",
            NetQty = 1m,
            AssociatedPurchaseQty = 1m,
        };
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }

    public class NoCallProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }
}

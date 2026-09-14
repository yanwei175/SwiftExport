using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
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
        CollectionAssert.Contains(changedProperties, nameof(NetDemandManagerViewModel.IsSourceSelected));
        CollectionAssert.Contains(changedProperties, nameof(NetDemandManagerViewModel.PageStatus));
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
    public async Task SaveSuggestedSupplier_ShouldSendOnlyNetDemandSuggestionAndVersion()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        var row = viewModel.Items.Single();
        var supplierId = Guid.NewGuid();

        row.ApplySuggestedSupplier(new ProcurementSupplierCandidateDto
        {
            Uid = supplierId,
            SupplierCode = "SUP-001",
            SupplierName = "测试供应商"
        });

        Assert.IsTrue(viewModel.HasUnsavedChanges);
        await viewModel.SaveSuggestedSuppliersCommand.ExecuteAsync(null);

        var command = api.SuggestedSupplierCommands.Single();
        Assert.AreEqual(row.Uid, command.NetDemandId);
        Assert.AreEqual(1, command.Version);
        Assert.AreEqual(supplierId, command.SuggestedPurchaseSupplierId);
        Assert.AreEqual(2, api.SearchRequests.Count, "保存成功后应重载工作台，以重新派生客供件建议送达方。");
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
            "NetDemandManagerView.xaml"));
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

        var suggestionColumns = workspace.Descendants(presentation + "DataGridTemplateColumn")
            .Where(element => string.Equals((string?)element.Attribute("Header"), "推荐采购供应商", StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(1, suggestionColumns.Length);
        Assert.IsFalse(workspace.Descendants(presentation + "DataGridTemplateColumn")
            .Any(element => string.Equals((string?)element.Attribute("Header"), "默认供应商", StringComparison.Ordinal)));
        var displayTemplate = suggestionColumns[0].Element(presentation + "DataGridTemplateColumn.CellTemplate");
        Assert.IsNotNull(displayTemplate);
        Assert.AreEqual(0, displayTemplate.Descendants().Count(element => element.Name.LocalName.EndsWith("LookupTextBox", StringComparison.Ordinal)));
        var editingLookup = suggestionColumns[0]
            .Element(presentation + "DataGridTemplateColumn.CellEditingTemplate")!
            .Descendants()
            .Single(element => element.Name.LocalName == "PurchaseSupplierLookupTextBox");
        StringAssert.Contains((string?)editingLookup.Attribute("SelectedSupplier") ?? string.Empty, "SuggestedPurchaseSupplierSelection");
        Assert.AreEqual("{Binding CommitSuggestedSupplierSelectionCommand}", (string?)editingLookup.Attribute("SelectionCommittedCommand"));
        Assert.AreEqual("{Binding ClearSuggestedSupplierSelectionCommand}", (string?)editingLookup.Attribute("SelectionClearedCommand"));
        Assert.IsNull(editingLookup.Attribute("Text"));

        var pagerTrigger = workspace.Descendants(presentation + "DataTrigger")
            .Single(element => ((string?)element.Attribute("Binding"))?.Contains("IsSourceSelected", StringComparison.Ordinal) == true);
        Assert.AreEqual("True", (string?)pagerTrigger.Attribute("Value"));
    }

    private static NetDemandManagerViewModel CreateViewModel(RecordingNetDemandApi api)
    {
        var runner = new DirectApiCallRunner();
        var messages = new MessageServiceStub();
        var dialogs = new DialogServiceStub();
        return new NetDemandManagerViewModel(
            api,
            DispatchProxy.Create<IProcurementMasterDataQueryApi, NoCallProxy>(),
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

    /// <summary>记录管理页调用；来源候选建立由独立选择器测试覆盖，误调用时直接失败。</summary>
    private sealed class RecordingNetDemandApi(Guid sourceUid) : INetDemandManagerApi
    {
        /// <summary>本夹具不承担候选分页查询。</summary>
        public Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
            SourceDemandCandidateSearchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本夹具不承担候选来源分组。</summary>
        public Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本夹具不承担候选来源行读取。</summary>
        public Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
            string sourceType, Guid sourceUid, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本夹具不承担来源建立命令。</summary>
        public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
            EstablishMaterialDemandsCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public List<MaterialDemandManagerSearchRequest> SearchRequests { get; } = [];
        public List<MaterialDemandSourceGroupSearchRequest> SourceGroupRequests { get; } = [];
        public List<RollbackMaterialDemandsCommand> RollbackCommands { get; } = [];
        public List<UpdateSuggestedPurchaseSupplierCommand> SuggestedSupplierCommands { get; } = [];

        public Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchAsync(
            MaterialDemandManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchRequests.Add(request);
            var sourceMode = request.SourceUid.HasValue;
            var items = sourceMode
                ? new[] { CreateItem(sourceUid, 1), CreateItem(sourceUid, 2) }
                : new[] { CreateItem(sourceUid, 1) };
            return Task.FromResult(ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Ok(new PagedResponseBase<MaterialDemandManagerItemDto>
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

        public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
            UpdateSuggestedPurchaseSupplierCommand command,
            CancellationToken cancellationToken = default)
        {
            SuggestedSupplierCommands.Add(command);
            return Task.FromResult(ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Ok(new UpdateSuggestedPurchaseSupplierResult
            {
                NetDemandId = command.NetDemandId,
                Version = command.Version + 1,
                SuggestedPurchaseSupplierId = command.SuggestedPurchaseSupplierId,
                SuggestedPurchaseSupplierCode = "SUP-001",
                SuggestedPurchaseSupplierName = "测试供应商"
            }));
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

        private static MaterialDemandManagerItemDto CreateItem(Guid sourceUid, int lineNumber) => new()
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

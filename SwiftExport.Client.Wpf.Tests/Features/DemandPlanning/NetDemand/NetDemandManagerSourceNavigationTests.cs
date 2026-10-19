using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>
/// 验证物料需求来源导航与更多条件 Popup 的关键交互契约。
/// </summary>
[TestClass]
public sealed partial class NetDemandManagerSourceNavigationTests
{
    /// <summary>逐行成功后结果未知及限流保持 B 行原请求，A 不重交且 C 只在 B 确认后发送。</summary>
    [TestMethod]
    public async Task SupplierSuggestion_PartialSuccessUnknownRateLimited_ResumesOriginalRow()
    {
        var sourceUid = Guid.NewGuid();
        var api = new RecordingNetDemandApi(sourceUid)
        {
            SearchItems = [
                new() { Uid = Guid.NewGuid(), Version = 1, GrossDemandId = Guid.NewGuid(), RootNetDemandId = Guid.NewGuid(), SourceUid = sourceUid, SourceType = "SO", NetQty = 1m },
                new() { Uid = Guid.NewGuid(), Version = 1, GrossDemandId = Guid.NewGuid(), RootNetDemandId = Guid.NewGuid(), SourceUid = sourceUid, SourceType = "SO", NetQty = 1m },
                new() { Uid = Guid.NewGuid(), Version = 1, GrossDemandId = Guid.NewGuid(), RootNetDemandId = Guid.NewGuid(), SourceUid = sourceUid, SourceType = "SO", NetQty = 1m }
            ]
        };
        api.SuggestionOutcomes.Enqueue(null);
        api.SuggestionOutcomes.Enqueue(ServiceErrorCodes.OutcomeUnknown);
        api.SuggestionOutcomes.Enqueue(ServiceErrorCodes.RateLimited);
        api.SuggestionOutcomes.Enqueue(null);
        api.SuggestionOutcomes.Enqueue(null);
        using var vm = CreateViewModel(api);
        await vm.InitializeAsync();
        for (var i = 0; i < 3; i++) vm.Items[i].ApplySuggestedSupplier(new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = $"S{i}", SupplierName = $"供应商{i}" });
        var ids = vm.Items.Select(x => x.Uid).ToArray();

        await vm.SaveSuggestedSuppliersCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPendingSuggestion);
        Assert.IsFalse(vm.CanEditSuggestion);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        Assert.AreEqual(2, api.SuggestedSupplierCommands.Count);
        Assert.AreEqual(ids[0], api.SuggestedSupplierCommands[0].NetDemandId);
        Assert.AreEqual(ids[1], api.SuggestedSupplierCommands[1].NetDemandId);

        await vm.SaveSuggestedSuppliersCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPendingSuggestion);
        await vm.SaveSuggestedSuppliersCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.HasPendingSuggestion);
        Assert.AreEqual(5, api.SuggestedSupplierCommands.Count);
        Assert.IsTrue(api.SuggestedSupplierCommands.Skip(1).Take(3).All(x => ReferenceEquals(x, api.SuggestedSupplierCommands[1])));
        Assert.AreEqual(ids[2], api.SuggestedSupplierCommands[4].NetDemandId);
        Assert.AreNotEqual(api.SuggestedSupplierCommands[0].CommandId, api.SuggestedSupplierCommands[1].CommandId);
        Assert.AreNotEqual(api.SuggestedSupplierCommands[1].CommandId, api.SuggestedSupplierCommands[4].CommandId);
    }

    /// <summary>首次请求限流只拒绝该次尝试，不把页面置为无法编辑的未决状态。</summary>
    [TestMethod]
    public async Task SupplierSuggestion_FirstRateLimited_AllowsNewAttempt()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        api.SuggestionOutcomes.Enqueue(ServiceErrorCodes.RateLimited);
        api.SuggestionOutcomes.Enqueue(null);
        using var vm = CreateViewModel(api);
        await vm.InitializeAsync();
        vm.Items.Single().ApplySuggestedSupplier(new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "S", SupplierName = "供应商" });
        await vm.SaveSuggestedSuppliersCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.HasPendingSuggestion);
        Assert.IsTrue(vm.CanEditSuggestion);
        await vm.SaveSuggestedSuppliersCommand.ExecuteAsync(null);
        Assert.AreNotEqual(api.SuggestedSupplierCommands[0].CommandId, api.SuggestedSupplierCommands[1].CommandId);
    }

    /// <summary>模块查询权限不会让建议列在缺少独立维护权限时进入编辑状态。</summary>
    [TestMethod]
    public async Task SupplierSuggestionPermission_DisablesEditorAndSave()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        using var vm = CreateViewModel(api, allowSuggestion: false);
        await vm.InitializeAsync();
        Assert.IsFalse(vm.CanEditSuggestion);
        vm.Items.Single().ApplySuggestedSupplier(new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "S", SupplierName = "供应商" });
        Assert.IsFalse(vm.SaveSuggestedSuppliersCommand.CanExecute(null));
    }

    /// <summary>未决门禁连接表格只读状态及实际 Lookup 控件，不依赖仅禁用工具栏按钮。</summary>
    [TestMethod]
    public void SupplierSuggestionEditor_BindsActualGridAndLookupGate()
    {
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf", "Features",
            "DemandPlanning", "NetDemand", "Views", "NetDemandManagerView.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grid = view.Descendants(ns + "DataGrid").Single();
        Assert.AreEqual("{Binding IsSuggestionReadOnly}", (string?)grid.Attribute("IsReadOnly"));
        var lookup = view.Descendants().Single(element => element.Name.LocalName == "PurchaseSupplierLookupTextBox");
        // 行门禁结合页面未决状态和行资格，不能仅检查旧页面属性名。
        Assert.AreEqual("{Binding IsSuggestionEditable}", (string?)lookup.Attribute("IsEnabled"));
    }
    /// <summary>来源查询独立于顶部条件，完整加载当前生命周期物料族，保持普通分页大小。</summary>
    [TestMethod]
    public async Task SelectingSource_ShouldUseSourceFactFilterAndKeepNormalPageSize()
    {
        var sourceUid = Guid.NewGuid();
        var api = new RecordingNetDemandApi(sourceUid);
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.Keyword = "SKU-SEARCH";
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
        Assert.AreEqual(string.Empty, sourceRequest.Keyword);
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

    /// <summary>父件、客供子件和同组兄弟必须同步选择，不得影响其他拆解树。</summary>
    [TestMethod]
    public async Task RowSelection_ShouldSynchronizeLoadedDemandFamily()
    {
        var sourceUid = Guid.NewGuid();
        var grossDemandId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var otherRootId = Guid.NewGuid();
        var api = new RecordingNetDemandApi(sourceUid)
        {
            SearchItems =
            [
                CreateManagerItem(sourceUid, grossDemandId, rootId, rootId, 1, false),
                CreateManagerItem(sourceUid, grossDemandId, rootId, Guid.NewGuid(), 2, true),
                CreateManagerItem(sourceUid, grossDemandId, rootId, Guid.NewGuid(), 3, true),
                CreateManagerItem(sourceUid, Guid.NewGuid(), otherRootId, otherRootId, 1, false),
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        var family = viewModel.Items.Where(item => item.RootNetDemandId == rootId).ToArray();
        var other = viewModel.Items.Single(item => item.RootNetDemandId == otherRootId);

        family[0].IsChecked = true;

        Assert.IsTrue(family.All(item => item.IsChecked));
        Assert.IsFalse(other.IsChecked);
        Assert.AreEqual(3, viewModel.CheckedCount);
        Assert.IsNull(viewModel.IsAllChecked);
        Assert.IsTrue(viewModel.RollbackSourceEstablishmentCommand.CanExecute(null));

        family[1].IsChecked = false;

        Assert.IsTrue(family.All(item => !item.IsChecked));
        Assert.AreEqual(0, viewModel.CheckedCount);
        Assert.AreEqual(false, viewModel.IsAllChecked);

        family[2].IsChecked = true;

        Assert.IsTrue(family.All(item => item.IsChecked));
        Assert.AreEqual(3, viewModel.CheckedCount);
    }

    /// <summary>表头全选和全取消继续覆盖当前页全部拆解组。</summary>
    [TestMethod]
    public async Task HeaderSelection_ShouldSelectAndClearAllLoadedFamilies()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid())
        {
            SearchItems =
            [
                CreateManagerItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, false),
                CreateManagerItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, false),
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();

        viewModel.IsAllChecked = true;

        Assert.IsTrue(viewModel.Items.All(item => item.IsChecked));
        Assert.AreEqual(viewModel.Items.Count, viewModel.CheckedCount);

        viewModel.IsAllChecked = false;

        Assert.IsTrue(viewModel.Items.All(item => !item.IsChecked));
        Assert.AreEqual(0, viewModel.CheckedCount);
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

    /// <summary>来源列表与多行筛选继续可用，推荐供应商编辑和来源分页门禁保持连接。</summary>
    [TestMethod]
    public void ManagerView_ShouldExposeSourceListAndFullWidthMultilinePopup()
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
        Assert.AreEqual(1, sourcePanel.Descendants().Count(element => element.Name.LocalName == "MultiSelectLookupButton"));
        StringAssert.Contains((string?)sourcePanel.Attribute("Visibility") ?? string.Empty, "IsSourcePanelOpen");

        var popup = workspace.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsMultiLineFilterOpen");
        Assert.AreEqual(presentation + "StackPanel", popup.Parent!.Name);
        StringAssert.Contains((string?)popup.Attribute("PlacementTarget") ?? string.Empty, "AncestorType={x:Type StackPanel}");
        Assert.AreEqual("{Binding PlacementTarget.ActualWidth, RelativeSource={RelativeSource Self}}", (string?)popup.Attribute("Width"));
        Assert.IsFalse(popup.Descendants(presentation + "StackPanel").Any(element => (string?)element.Attribute("Width") == "740"));
        Assert.AreEqual("False", (string?)popup.Attribute("StaysOpen"));
        var textBoxes = popup.Descendants().Where(element => element.Name.LocalName == "TextBox"
            && (string?)element.Attribute("AcceptsReturn") == "True").ToArray();
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

    private static NetDemandManagerViewModel CreateViewModel(RecordingNetDemandApi api, bool allowSuggestion = true,
        IDialogService? dialog = null, ICurrentUserService? currentUser = null, INavigationService? navigation = null,
        INetDemandDialogService? netDialog = null)
    {
        var runner = new DirectApiCallRunner();
        var messages = new MessageServiceStub();
        var dialogs = dialog ?? new DialogServiceStub();
        var user = DispatchProxy.Create<ICurrentUserService, PermissionProxy>();
        ((PermissionProxy)(object)user).Allow = allowSuggestion;
        return new NetDemandManagerViewModel(
            api,
            DispatchProxy.Create<IProcurementMasterDataQueryApi, NoCallProxy>(),
            runner,
            dialogs,
            messages,
            netDialog ?? DispatchProxy.Create<INetDemandDialogService, NoCallProxy>(),
            currentUser ?? user, navigation);
    }

    /// <summary>创建父子联动测试使用的物料工作台行。</summary>
    private static MaterialDemandManagerItemDto CreateManagerItem(
        Guid sourceUid,
        Guid grossDemandId,
        Guid rootNetDemandId,
        Guid uid,
        int lineNumber,
        bool isCustomerSupplied)
        => new()
        {
            Uid = uid,
            Version = 1,
            GrossDemandId = grossDemandId,
            DecompositionRevision = 1,
            LineNumber = lineNumber,
            SourceType = "SO",
            SourceUid = sourceUid,
            SourceNo = "SO-1001",
            RootNetDemandId = rootNetDemandId,
            IsCustomerSupplied = isCustomerSupplied,
            Sku = $"SKU-{lineNumber}",
            NetQty = 1m,
        };

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
        public Queue<string?> SuggestionOutcomes { get; } = new();
        public Queue<string?> RollbackOutcomes { get; } = new();
        public Func<MaterialDemandManagerSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>>>? SearchHandler { get; set; }
        public Func<MaterialDemandSourceGroupSearchRequest, CancellationToken, Task<ServiceResult<List<NetDemandSourceGroupDto>>>>? GroupHandler { get; set; }
        /// <summary>指定测试需要的当前页物料行；为空时使用默认夹具。</summary>
        public List<MaterialDemandManagerItemDto>? SearchItems { get; init; }

        public Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchAsync(
            MaterialDemandManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchRequests.Add(request);
            if (SearchHandler is not null) return SearchHandler(request, cancellationToken);
            var sourceMode = request.SourceUid.HasValue;
            var items = SearchItems?.ToArray() ?? (sourceMode
                ? [CreateItem(sourceUid, 1), CreateItem(sourceUid, 2)]
                : [CreateItem(sourceUid, 1)]);
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
            if (GroupHandler is not null) return GroupHandler(request, cancellationToken);
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
            if (SuggestionOutcomes.Count > 0 && SuggestionOutcomes.Dequeue() is { } code)
                return Task.FromResult(ServiceResult<UpdateSuggestedPurchaseSupplierResult>.Fail(code, code));
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
            if (RollbackOutcomes.Count > 0 && RollbackOutcomes.Dequeue() is { } code)
                return Task.FromResult(ServiceResult<RollbackMaterialDemandsResult>.Fail(code, "SO-1001 / SKU-1：有效执行阻断，不能回退。"));
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

    /// <summary>仅向当前工作台提供允许权限的内存会话。</summary>
    public class PermissionProxy : DispatchProxy
    {
        public bool Allow { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "HasPermission" => Allow,
            "get_CurrentUser" or "add_CurrentUserChanged" or "remove_CurrentUserChanged" => null,
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default)
        {
            ((MaterialDemandRollbackReasonViewModel)content).Reason = "重新核对物料组成";
            return Task.FromResult(DialogResult.Primary);
        }
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

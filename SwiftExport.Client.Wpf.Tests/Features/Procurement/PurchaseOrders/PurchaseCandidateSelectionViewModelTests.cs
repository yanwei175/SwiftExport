using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Operations;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Components;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

[TestClass]
[DoNotParallelize]
public sealed class PurchaseCandidateSelectionViewModelTests
{
    /// <summary>推荐、手动选择、复合填充和清空都必须维护同一组实际供应商事实。</summary>
    [TestMethod]
    public void SupplierFacts_ShouldRemainConsistentAcrossEveryInputPath()
    {
        var recommendedId = Guid.NewGuid();
        var row = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            SourceType = "SO",
            SourceUid = Guid.NewGuid(),
            SourceNo = "SO-1",
            SupplyType = SupplyTypeEnums.采购,
            SuggestedPurchaseSupplierId = recommendedId,
            SuggestedPurchaseSupplierCode = "SUP-A",
            SuggestedPurchaseSupplierName = "供应商甲",
            NetQty = 1m,
        });

        Assert.AreEqual(recommendedId, row.SupplierId);
        Assert.AreEqual("SUP-A", row.PurchaseSupplierCode);
        Assert.AreEqual("供应商甲", row.PurchaseSupplierName);
        Assert.AreEqual(recommendedId, row.PurchaseSupplierSelection!.Uid);

        var manualId = Guid.NewGuid();
        row.PurchaseSupplierSelection = new ProcurementSupplierCandidateDto
        {
            Uid = manualId,
            SupplierCode = "SUP-B",
            SupplierName = "供应商乙",
        };
        Assert.AreEqual(manualId, row.SupplierId);
        Assert.AreEqual("SUP-B 供应商乙", row.PurchaseSupplierDisplay);

        var filledId = Guid.NewGuid();
        row.SupplierId = filledId;
        row.PurchaseSupplierCode = "SUP-C";
        row.PurchaseSupplierName = "供应商丙";
        Assert.AreEqual(filledId, row.PurchaseSupplierSelection!.Uid);
        Assert.AreEqual("SUP-C", row.PurchaseSupplierSelection.SupplierCode);
        Assert.AreEqual("供应商丙", row.PurchaseSupplierSelection.SupplierName);

        row.ClearPurchaseSupplier();
        Assert.AreEqual(Guid.Empty, row.SupplierId);
        Assert.AreEqual(string.Empty, row.PurchaseSupplierCode);
        Assert.AreEqual(string.Empty, row.PurchaseSupplierName);
        Assert.IsNull(row.PurchaseSupplierSelection);
        Assert.AreEqual("未选择", row.PurchaseSupplierDisplay);

        row.PurchaseSupplierName = "只有名称";
        Assert.AreEqual(Guid.Empty, row.SupplierId);
        Assert.AreEqual("未选择", row.PurchaseSupplierDisplay);
    }

    /// <summary>送达方许可必须同时服从客供属性和页面输入门禁。</summary>
    [TestMethod]
    public void SupplierEditability_ShouldUseTheSameRowGateAsFillDown()
    {
        var standard = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(), Version = 1, SourceType = "SO", SourceUid = Guid.NewGuid(),
            SourceNo = "SO-1", SupplyType = SupplyTypeEnums.采购, NetQty = 1m,
        });
        var customerSupplied = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(), Version = 1, SourceType = "SO", SourceUid = Guid.NewGuid(),
            SourceNo = "SO-1", SupplyType = SupplyTypeEnums.采购, NetQty = 1m,
            IsCustomerSupplied = true,
        });

        Assert.IsTrue(standard.CanEditPurchaseSupplier);
        Assert.IsFalse(standard.CanEditDeliverySupplier);
        Assert.IsTrue(customerSupplied.CanEditDeliverySupplier);

        standard.IsInputEnabled = false;
        customerSupplied.IsInputEnabled = false;
        Assert.IsFalse(standard.CanEditPurchaseSupplier);
        Assert.IsFalse(customerSupplied.CanEditPurchaseSupplier);
        Assert.IsFalse(customerSupplied.CanEditDeliverySupplier);
    }

    /// <summary>真实采购页面必须由原生 DataGrid 进入编辑态，并把 Lookup 选择提交到当前行。</summary>
    [TestMethod]
    public void PurchaseSupplierColumn_ShouldBeginEditFocusLookupAndCommitStableIdentity()
    {
        StaTest.RunAsync(() =>
        {
            ApplicationResourceLoader.Load();
            using var viewModel = CreateViewModel(new CandidateApiStub());
            var original = CreateSupplier("SUP-A", "供应商甲");
            var replacement = CreateSupplier("SUP-B", "供应商乙");
            var row = CreateCandidateRow(original);
            viewModel.Items.Add(row);
            viewModel.SupplierCandidates.Add(replacement);
            var view = new PurchaseCandidateSelectionView { DataContext = viewModel };
            var window = Show(view);

            try
            {
                var grid = (DataGrid)view.FindName("CandidateGrid");
                var column = FindColumn(grid, "PurchaseSupplier");
                Assert.IsFalse(grid.IsReadOnly);
                Assert.IsFalse(column.IsReadOnly);
                Assert.IsTrue(row.CanEditPurchaseSupplier);

                grid.CurrentCell = new DataGridCellInfo(row, column);
                grid.Focus();
                Assert.IsTrue(grid.BeginEdit(), "供应商模板列应允许原生双击/F2对应的 BeginEdit。 ");
                PumpDispatcher();

                var lookup = FindVisualDescendants<PurchaseSupplierLookupTextBox>(grid).Single();
                var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
                var input = (TextBox)editor.FindName("InputTextBox");
                var results = (ListBox)editor.FindName("ResultList");
                Assert.IsTrue(input.IsKeyboardFocusWithin, "进入编辑态后键盘焦点应落入 Lookup 输入框。 ");

                editor.IsDropDownOpen = true;
                results.SelectedItem = replacement;
                RaisePreviewKey(input, Key.Enter);
                PumpDispatcher();

                Assert.AreEqual(replacement.Uid, row.SupplierId);
                Assert.AreEqual(replacement.SupplierCode, row.PurchaseSupplierCode);
                Assert.AreEqual(replacement.SupplierName, row.PurchaseSupplierName);
                Assert.AreEqual(replacement.Uid, row.PurchaseSupplierSelection?.Uid);

                input.Text = "未提交文本";
                RaisePreviewKey(input, Key.Escape);
                grid.CancelEdit(DataGridEditingUnit.Cell);
                PumpDispatcher();
                Assert.AreEqual(replacement.Uid, row.SupplierId, "Esc 不得把未提交文本冒充供应商身份。 ");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>真实 EnterpriseGrid 命令必须复合填充供应商事实、跳过禁用行并一次撤销。</summary>
    [TestMethod]
    public void PurchaseSupplierColumn_CtrlDShouldFillCompositeFactsSkipDisabledAndUndoTogether()
    {
        StaTest.RunAsync(() =>
        {
            ApplicationResourceLoader.Load();
            using var viewModel = CreateViewModel(new CandidateApiStub());
            var sourceSupplier = CreateSupplier("SUP-A", "供应商甲");
            var first = CreateCandidateRow(sourceSupplier);
            var secondOriginal = CreateSupplier("SUP-B", "供应商乙");
            var second = CreateCandidateRow(secondOriginal);
            var disabledOriginal = CreateSupplier("SUP-C", "供应商丙");
            var disabled = CreateCandidateRow(disabledOriginal);
            disabled.IsInputEnabled = false;
            viewModel.Items.Add(first);
            viewModel.Items.Add(second);
            viewModel.Items.Add(disabled);
            var view = new PurchaseCandidateSelectionView { DataContext = viewModel };
            var window = Show(view);

            try
            {
                var grid = (DataGrid)view.FindName("CandidateGrid");
                var column = FindColumn(grid, "PurchaseSupplier");
                grid.SelectedCells.Clear();
                foreach (var row in new[] { first, second, disabled })
                    grid.SelectedCells.Add(new DataGridCellInfo(row, column));
                grid.CurrentCell = new DataGridCellInfo(first, column);
                grid.Focus();
                PumpDispatcher();

                Assert.IsTrue(GridOperations.FillDownCommand.CanExecute(null, grid));
                GridOperations.FillDownCommand.Execute(null, grid);
                PumpDispatcher();

                Assert.AreEqual(sourceSupplier.Uid, second.SupplierId);
                Assert.AreEqual(sourceSupplier.SupplierCode, second.PurchaseSupplierCode);
                Assert.AreEqual(sourceSupplier.SupplierName, second.PurchaseSupplierName);
                Assert.AreEqual(sourceSupplier.Uid, second.PurchaseSupplierSelection?.Uid);
                Assert.AreEqual(disabledOriginal.Uid, disabled.SupplierId, "禁用目标行必须由 CanFillDownPath 跳过。 ");

                Assert.IsTrue(ApplicationCommands.Undo.CanExecute(null, grid));
                ApplicationCommands.Undo.Execute(null, grid);
                PumpDispatcher();
                Assert.AreEqual(secondOriginal.Uid, second.SupplierId);
                Assert.AreEqual(secondOriginal.SupplierCode, second.PurchaseSupplierCode);
                Assert.AreEqual(secondOriginal.SupplierName, second.PurchaseSupplierName);
                Assert.AreEqual(secondOriginal.Uid, second.PurchaseSupplierSelection?.Uid);

                grid.CurrentCell = new DataGridCellInfo(disabled, column);
                grid.Focus();
                Assert.IsFalse(grid.BeginEdit(), "与填充许可相同的禁用行不得进入供应商编辑态。 ");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Open_ShouldOnlyLoadGroups_AndKeepDetailEmpty()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 205));
            using var viewModel = CreateViewModel(api);

            await viewModel.OpenAsync();

            Assert.AreEqual(1, api.GroupCallCount);
            Assert.AreEqual(0, api.DetailRequests.Count);
            Assert.AreEqual(2, viewModel.SourceGroups.Count);
            Assert.IsTrue(viewModel.SourceGroups[0].IsAllSources);
            Assert.IsNull(viewModel.SelectedSourceGroup);
            Assert.AreEqual(0, viewModel.Items.Count);
            StringAssert.Contains(viewModel.StatusText, "请选择左侧来源");
        });
    }

    [TestMethod]
    public void SourceAndAllSelection_ShouldLoadEveryCandidateWithoutPaging()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 205));
            api.Candidates.AddRange(Enumerable.Range(1, 205)
                .Select(index => CreateCandidate(api.FirstSourceUid, "SO-1", index)));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            Assert.AreEqual(205, viewModel.Items.Count);
            Assert.AreEqual("SO", api.DetailRequests[0].SourceType);
            Assert.AreEqual(api.FirstSourceUid, api.DetailRequests[0].SourceUid);

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
            Assert.AreEqual(205, viewModel.Items.Count);
            Assert.IsNull(api.DetailRequests[1].SourceUid);
        });
    }

    [TestMethod]
    public void LocalFilterAndToggleAll_ShouldNotQuery_AndShouldPreserveHiddenSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 2));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-1", 1, "MODEL-A", "供应商甲"));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-1", 2, "MODEL-B", "供应商乙"));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
            var callsAfterLoad = api.DetailRequests.Count;

            viewModel.Items[0].IsChecked = true;
            viewModel.Keyword = "MODEL-B";
            viewModel.SourceNo = "SO-1";

            Assert.AreEqual(callsAfterLoad, api.DetailRequests.Count);
            Assert.AreEqual(1, viewModel.FilteredCount);
            Assert.AreEqual(1, viewModel.SelectedCount);
            viewModel.ToggleAllVisibleCommand.Execute(null);
            Assert.AreEqual(2, viewModel.SelectedCount);
            Assert.IsTrue(viewModel.Items.All(static item => item.IsChecked));
        });
    }

    [TestMethod]
    public void RapidSwitchAndClose_ShouldRejectStaleResults()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var secondSourceUid = Guid.NewGuid();
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
            api.Groups.Add(CreateGroup("SO-B", secondSourceUid, 1));
            var firstCompletion = new TaskCompletionSource<ServiceResult<List<PurchaseDraftCandidateDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.DetailHandler = (request, _) => request.SourceUid == api.FirstSourceUid
                ? firstCompletion.Task
                : Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(
                    [CreateCandidate(secondSourceUid, "SO-B", 2)]));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();

            var firstLoad = viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            await Task.Yield();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[2]);
            firstCompletion.SetResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(
                [CreateCandidate(api.FirstSourceUid, "SO-A", 1)]));
            await firstLoad;

            Assert.AreEqual("SO-B", viewModel.Items.Single().SourceNo);
            var lateCompletion = new TaskCompletionSource<ServiceResult<List<PurchaseDraftCandidateDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.DetailHandler = (_, _) => lateCompletion.Task;
            var closingLoad = viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            await Task.Yield();
            viewModel.Close();
            lateCompletion.SetResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(
                [CreateCandidate(api.FirstSourceUid, "SO-A", 3)]));
            await closingLoad;
            Assert.AreEqual(0, viewModel.Items.Count);
        });
    }

    [TestMethod]
    public void SwitchWithSelection_ShouldAllowCancelWithoutLosingRows()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var secondSourceUid = Guid.NewGuid();
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
            api.Groups.Add(CreateGroup("SO-B", secondSourceUid, 1));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-A", 1));
            using var viewModel = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            viewModel.Items.Single().IsChecked = true;
            var callsBeforeSwitch = api.DetailRequests.Count;

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[2]);
            Assert.IsTrue(viewModel.IsSourceSwitchPending);
            viewModel.CancelSourceSwitchCommand.Execute(null);

            Assert.AreEqual(callsBeforeSwitch, api.DetailRequests.Count);
            Assert.AreEqual("SO-A", viewModel.Items.Single().SourceNo);
            Assert.AreEqual(1, viewModel.SelectedCount);
            Assert.AreSame(viewModel.SourceGroups[1], viewModel.SelectedSourceGroup);
        });
    }

    private static PurchaseCandidateSelectionViewModel CreateViewModel(CandidateApiStub api) =>
        new(api, new DirectApiCallRunner());

    private static ProcurementSupplierCandidateDto CreateSupplier(string code, string name) => new()
    {
        Uid = Guid.NewGuid(),
        SupplierCode = code,
        SupplierName = name,
    };

    private static PurchaseCandidateItemModel CreateCandidateRow(ProcurementSupplierCandidateDto supplier) =>
        PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            SourceType = "SO",
            SourceUid = Guid.NewGuid(),
            SourceNo = "SO-1",
            Sku = "SKU-1",
            SupplyType = SupplyTypeEnums.采购,
            NetQty = 1m,
            SuggestedPurchaseSupplierId = supplier.Uid,
            SuggestedPurchaseSupplierCode = supplier.SupplierCode,
            SuggestedPurchaseSupplierName = supplier.SupplierName,
        });

    private static DataGridTemplateColumn FindColumn(DataGrid grid, string key) =>
        (DataGridTemplateColumn)grid.Columns.Single(column => GridColumn.GetKey(column) == key);

    private static Window Show(FrameworkElement content)
    {
        var window = new Window
        {
            Width = 1500,
            Height = 720,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content,
        };
        window.Show();
        window.Activate();
        PumpDispatcher();
        return window;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private static PurchaseDraftCandidateSourceGroupDto CreateGroup(string sourceNo, Guid sourceUid, int count) => new()
    {
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        CandidateCount = count,
    };

    private static PurchaseDraftCandidateDto CreateCandidate(
        Guid sourceUid,
        string sourceNo,
        int line,
        string? linkSku = null,
        string? supplierName = null) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        GrossDemandId = Guid.NewGuid(),
        LineNumber = line,
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        SourceLineId = Guid.NewGuid(),
        SourceLineNumber = line,
        RootNetDemandId = Guid.NewGuid(),
        Sku = $"SKU-{line}",
        LinkSku = linkSku ?? $"MODEL-{line}",
        Description = $"物料 {line}",
        SupplyType = SupplyTypeEnums.采购,
        SuggestedPurchaseSupplierName = supplierName ?? "测试供应商",
        NetQty = 10,
        AssociatedPurchaseQty = 2,
    };

    private sealed class CandidateApiStub : IPurchaseDraftApi
    {
        public Guid FirstSourceUid { get; } = Guid.NewGuid();
        public List<PurchaseDraftCandidateSourceGroupDto> Groups { get; } = [];
        public List<PurchaseDraftCandidateDto> Candidates { get; } = [];
        public List<PurchaseDraftCandidateQuery> DetailRequests { get; } = [];
        public int GroupCallCount { get; private set; }
        public Func<PurchaseDraftCandidateQuery, CancellationToken, Task<ServiceResult<List<PurchaseDraftCandidateDto>>>>? DetailHandler { get; set; }

        public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default)
        {
            GroupCallCount++;
            return Task.FromResult(ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Ok(Groups.ToList()));
        }

        public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(request);
            if (DetailHandler is not null) return DetailHandler(request, cancellationToken);
            var rows = request.SourceUid.HasValue
                ? Candidates.Where(item => item.SourceUid == request.SourceUid
                    && string.Equals(item.SourceType, request.SourceType, StringComparison.Ordinal)).ToList()
                : Candidates.ToList();
            return Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(rows));
        }

        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(Guid uid, CancelPurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(Guid uid, PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(Guid uid, ReleasePurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

}

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
using SwiftExport.Wpf.EnterpriseGrid.Validation;
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

    /// <summary>业务阻断只作为行级错误供行头与摘要使用，不把每个绑定属性都标成红框。</summary>
    [TestMethod]
    public void BusinessValidation_ShouldExposeRowErrorWithoutPropertyErrors()
    {
        var row = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
        {
            Uid = Guid.NewGuid(), Version = 1, SourceType = "SO", SourceUid = Guid.NewGuid(),
            SourceNo = "SO-1", SupplyType = SupplyTypeEnums.采购, NetQty = 1m,
        });
        row.PurchaseQty = 0m;

        Assert.IsTrue(row.HasErrors);
        Assert.IsTrue(row.GetErrors(null).Cast<object>().Any());
        Assert.IsFalse(row.GetErrors(nameof(row.PurchaseQty)).Cast<object>().Any());
        StringAssert.Contains(row.ValidationDisplayMessage, "采购数量必须大于 0");
    }

    /// <summary>真实采购 Grid 聚合行级业务错误但不向各业务单元格分发软错误边框，并仍可定位错误行。</summary>
    [TestMethod]
    public void BusinessValidation_ShouldRemainRowScopedInPurchaseGrid()
    {
        StaTest.RunAsync(() =>
        {
            ApplicationResourceLoader.Load();
            using var viewModel = CreateViewModel(new CandidateApiStub());
            var row = PurchaseCandidateItemModel.FromDto(new PurchaseDraftCandidateDto
            {
                Uid = Guid.NewGuid(), Version = 1, SourceType = "SO", SourceUid = Guid.NewGuid(),
                SourceNo = "SO-1", Sku = "SKU-1", SupplyType = SupplyTypeEnums.采购, NetQty = 1m,
            });
            row.PurchaseQty = 0m;
            viewModel.Items.Add(row);
            var view = new PurchaseCandidateSelectionView { DataContext = viewModel };
            var window = Show(view);
            try
            {
                var grid = (DataGrid)view.FindName("CandidateGrid");
                PumpDispatcher();
                var errors = GridValidation.GetErrors(grid);
                Assert.IsTrue(errors.Any(error => ReferenceEquals(error.Item, row)
                    && error.ColumnKey is null
                    && error.Message.Contains("采购数量必须大于 0", StringComparison.Ordinal)));
                Assert.IsFalse(FindVisualDescendants<DataGridCell>(grid)
                    .Where(cell => ReferenceEquals(cell.DataContext, row))
                    .Any(cell => GridValidation.GetCellState(cell).HasError));
                Assert.IsTrue(GridValidation.FocusFirstError(grid));
                Assert.AreSame(row, grid.SelectedItem);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        });
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
            viewModel.ToggleAllVisibleCommand.Execute(null);
            Assert.IsTrue(viewModel.Items[0].IsChecked, "隐藏行不受表头全选/全不选影响。 ");
            Assert.IsFalse(viewModel.Items[1].IsChecked);
        });
    }

    /// <summary>直接勾选父件只联动同来源真实关联客供件，即使关联行被本地筛选隐藏也必须生效。</summary>
    [TestMethod]
    public void DirectParentSelection_ShouldSelectOnlyRelatedCustomerSuppliedRowsInSameSource()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var otherSourceUid = Guid.NewGuid();
            var parent = CreateCandidate(api.FirstSourceUid, "SO-A", 1, "PARENT-VISIBLE");
            var related = CreateCandidate(api.FirstSourceUid, "SO-A", 2, "CHILD-HIDDEN");
            related.IsCustomerSupplied = true;
            related.DeliveryTargetNetDemandId = parent.Uid;
            var otherSource = CreateCandidate(otherSourceUid, "SO-B", 3, "CROSS-HIDDEN");
            otherSource.IsCustomerSupplied = true;
            otherSource.DeliveryTargetNetDemandId = parent.Uid;
            var unrelated = CreateCandidate(api.FirstSourceUid, "SO-A", 4, "OTHER-HIDDEN");
            unrelated.IsCustomerSupplied = true;
            unrelated.DeliveryTargetNetDemandId = Guid.NewGuid();
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 3));
            api.Groups.Add(CreateGroup("SO-B", otherSourceUid, 1));
            api.Candidates.AddRange([parent, related, otherSource, unrelated]);
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
            viewModel.Keyword = "PARENT-VISIBLE";
            var relatedRow = viewModel.Items.Single(item => item.Uid == related.Uid);
            relatedRow.PurchaseQty = 4m;
            relatedRow.UnitPrice = 12.5m;

            viewModel.Items.Single(item => item.Uid == parent.Uid).IsChecked = true;

            Assert.IsTrue(relatedRow.IsChecked);
            Assert.AreEqual(4m, relatedRow.PurchaseQty);
            Assert.AreEqual(12.5m, relatedRow.UnitPrice);
            Assert.IsFalse(viewModel.Items.Single(item => item.Uid == otherSource.Uid).IsChecked);
            Assert.IsFalse(viewModel.Items.Single(item => item.Uid == unrelated.Uid).IsChecked);
            Assert.AreEqual(2, viewModel.SelectedCount);
            StringAssert.Contains(viewModel.StatusText, "已联动选择 1 条");

            relatedRow.IsChecked = false;
            Assert.IsFalse(relatedRow.IsChecked, "用户单独取消客供件后不得被已勾选父件立即选回。 ");
            relatedRow.IsChecked = true;
            viewModel.Items.Single(item => item.Uid == parent.Uid).IsChecked = false;
            Assert.IsTrue(relatedRow.IsChecked, "取消父件不得撤掉用户独立保留的客供件。 ");
        });
    }

    /// <summary>顶部反选逐行取反且抑制父件级联；隐藏项不变，两次反选恢复原状态。</summary>
    [TestMethod]
    public void InvertVisibleSelection_ShouldPreserveHiddenRowsAndSuppressParentCascade()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var parent = CreateCandidate(api.FirstSourceUid, "SO-A", 1, "VISIBLE-PARENT");
            var child = CreateCandidate(api.FirstSourceUid, "SO-A", 2, "VISIBLE-CHILD");
            child.IsCustomerSupplied = true;
            child.DeliveryTargetNetDemandId = parent.Uid;
            var hidden = CreateCandidate(api.FirstSourceUid, "SO-A", 3, "HIDDEN");
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 3));
            api.Candidates.AddRange([parent, child, hidden]);
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            var parentRow = viewModel.Items.Single(item => item.Uid == parent.Uid);
            var childRow = viewModel.Items.Single(item => item.Uid == child.Uid);
            var hiddenRow = viewModel.Items.Single(item => item.Uid == hidden.Uid);
            childRow.IsChecked = true;
            hiddenRow.IsChecked = true;
            viewModel.Keyword = "VISIBLE";
            var inputChanged = 0;
            viewModel.InputChanged += (_, _) => inputChanged++;

            viewModel.InvertVisibleSelectionCommand.Execute(null);
            Assert.IsTrue(parentRow.IsChecked);
            Assert.IsFalse(childRow.IsChecked, "反选中的父件不得级联把同批次刚取消的客供件选回。 ");
            Assert.IsTrue(hiddenRow.IsChecked);

            viewModel.InvertVisibleSelectionCommand.Execute(null);
            Assert.IsFalse(parentRow.IsChecked);
            Assert.IsTrue(childRow.IsChecked);
            Assert.IsTrue(hiddenRow.IsChecked);
            Assert.AreEqual(2, inputChanged, "每次批量反选只能发布一次选择失效通知。 ");

            viewModel.SetInputEnabled(false);
            Assert.IsFalse(viewModel.InvertVisibleSelectionCommand.CanExecute(null));
            Assert.IsFalse(viewModel.ToggleAllVisibleCommand.CanExecute(null));
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

    /// <summary>恢复候选时更新服务端事实，同时保留采购输入并显式暴露超额错误。</summary>
    [TestMethod]
    public async Task RestoreCurrentScope_ShouldMergeAllFactsWithoutOverwritingPurchaseInputs()
    {
        var api = new CandidateApiStub();
        var original = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        var originallyUnchecked = CreateCandidate(api.FirstSourceUid, "SO-A", 2);
        api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 2));
        api.Candidates.AddRange([original, originallyUnchecked]);
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync();
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
        var row = viewModel.Items.Single(item => item.Uid == original.Uid);
        row.IsChecked = true;
        row.PurchaseQty = 7m;
        row.UnitPrice = 12.3m;
        row.TaxPrice = 13.9m;
        var plannedDate = new DateTime(2026, 10, 1);
        row.PlannedDeliveryDate = plannedDate;

        var refreshed = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        refreshed.Uid = original.Uid;
        refreshed.Version = 2;
        refreshed.NetQty = 6m;
        refreshed.AssociatedPurchaseQty = 1m;
        var refreshedUnchecked = CreateCandidate(api.FirstSourceUid, "SO-A", 2);
        refreshedUnchecked.Uid = originallyUnchecked.Uid;
        refreshedUnchecked.Version = 3;
        var added = CreateCandidate(api.FirstSourceUid, "SO-A", 3);
        api.DetailHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(
            [refreshed, refreshedUnchecked, added]));

        Assert.IsTrue(await viewModel.RestoreCurrentScopeAsync([]));

        row = viewModel.Items.Single(item => item.Uid == original.Uid);
        Assert.AreEqual(2, row.Version);
        Assert.AreEqual(5m, row.RemainingQty);
        Assert.AreEqual(7m, row.PurchaseQty);
        Assert.AreEqual(12.3m, row.UnitPrice);
        Assert.AreEqual(13.9m, row.TaxPrice);
        Assert.AreEqual(plannedDate, row.PlannedDeliveryDate);
        Assert.IsTrue(row.IsChecked);
        Assert.IsTrue(row.HasValidationError);
        Assert.AreEqual(3, viewModel.Items.Single(item => item.Uid == originallyUnchecked.Uid).Version);
        Assert.IsFalse(viewModel.Items.Single(item => item.Uid == added.Uid).IsChecked);
        Assert.AreEqual(api.FirstSourceUid, api.DetailRequests.Last().SourceUid);
        Assert.AreEqual("SO", api.DetailRequests.Last().SourceType);
    }

    /// <summary>当前待采购范围缺失的已选需求保留原输入，并要求用户显式取消选择。</summary>
    [TestMethod]
    public async Task RestoreCurrentScope_ShouldRetainMissingSelectedAndRemoveMissingUnchecked()
    {
        var api = new CandidateApiStub();
        var original = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        var uncheckedRow = CreateCandidate(api.FirstSourceUid, "SO-A", 2);
        api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 2));
        api.Candidates.AddRange([original, uncheckedRow]);
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync();
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
        var row = viewModel.Items.Single(item => item.Uid == original.Uid);
        row.IsChecked = true;
        row.PurchaseQty = 3m;
        api.DetailHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([]));

        Assert.IsTrue(await viewModel.RestoreCurrentScopeAsync([]));

        Assert.AreSame(row, viewModel.Items.Single());
        Assert.AreEqual(3m, row.PurchaseQty);
        Assert.IsTrue(row.IsChecked);
        Assert.IsFalse(row.IsAvailableForPurchase);
        StringAssert.Contains(row.ValidationDisplayMessage, "不在当前待采购范围");
    }

    /// <summary>全部来源入口恢复时保持空来源筛选，并按同一规则接纳新出现候选。</summary>
    [TestMethod]
    public async Task RestoreAllSources_ShouldQueryFullScopeAndLeaveNewRowsUnchecked()
    {
        var api = new CandidateApiStub();
        api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
        api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-A", 1));
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync();
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        viewModel.Items.Single().IsChecked = true;
        var added = CreateCandidate(Guid.NewGuid(), "SO-B", 2);
        api.DetailHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok(
            [api.Candidates.Single(), added]));

        Assert.IsTrue(await viewModel.RestoreCurrentScopeAsync([$"SO|{api.FirstSourceUid:N}"]));

        Assert.IsFalse(viewModel.Items.Single(item => item.Uid == api.Candidates.Single().Uid).IsChecked);
        Assert.IsFalse(viewModel.Items.Single(item => item.Uid == added.Uid).IsChecked);
        Assert.IsNull(api.DetailRequests.Last().SourceUid);
        Assert.AreEqual(string.Empty, api.DetailRequests.Last().SourceType);
    }

    /// <summary>恢复失败保持原集合可重试；关闭后取消请求并拒绝迟到结果覆盖旧事实。</summary>
    [TestMethod]
    public async Task RestoreCurrentScope_ShouldPreserveOnFailureAndRejectLateResultAfterClose()
    {
        var api = new CandidateApiStub();
        var original = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
        api.Candidates.Add(original);
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenAsync();
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
        var row = viewModel.Items.Single();
        row.IsChecked = true;
        row.PurchaseQty = 3m;
        api.DetailHandler = (_, _) => Task.FromResult(
            ServiceResult<List<PurchaseDraftCandidateDto>>.Fail("网络失败"));

        Assert.IsFalse(await viewModel.RestoreCurrentScopeAsync([]));
        Assert.AreSame(row, viewModel.Items.Single());
        Assert.AreEqual(3m, row.PurchaseQty);

        var retried = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        retried.Uid = original.Uid;
        retried.Version = 2;
        api.DetailHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([retried]));
        Assert.IsTrue(await viewModel.RestoreCurrentScopeAsync([]));
        Assert.AreEqual(2, viewModel.Items.Single().Version);
        Assert.AreEqual(3m, viewModel.Items.Single().PurchaseQty);

        var late = new TaskCompletionSource<ServiceResult<List<PurchaseDraftCandidateDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        api.DetailHandler = (_, _) => late.Task;
        var restoreTask = viewModel.RestoreCurrentScopeAsync([]);
        await Task.Yield();
        viewModel.Close();
        var refreshed = CreateCandidate(api.FirstSourceUid, "SO-A", 1);
        refreshed.Uid = original.Uid;
        refreshed.Version = 9;
        late.SetResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([refreshed]));

        Assert.IsFalse(await restoreTask);
        Assert.AreEqual(2, viewModel.Items.Single().Version);
        Assert.AreEqual(3m, viewModel.Items.Single().PurchaseQty);
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

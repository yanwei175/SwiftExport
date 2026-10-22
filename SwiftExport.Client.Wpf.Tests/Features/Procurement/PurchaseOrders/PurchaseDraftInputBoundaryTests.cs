using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

/// <summary>采购需求转草稿的追加权限、数量价格边界和原生编辑显示回归。</summary>
public sealed partial class PurchaseDraftWorkflowViewModelTests
{
    /// <summary>工作流权限变化必须通知外层按钮，撤销创建后不能执行，恢复后新建无需编辑权限。</summary>
    [TestMethod]
    public void PurchaseInput_OuterCreateTracksPermissionChanges() => TestInfrastructure.StaTest.RunAsync(async () =>
    {
        var api = new DraftApi(); var user = new CurrentUserStub(true); using var workflow = Create(api, user: user);
        workflow.BeginSession(); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        picker.AttachWorkflow(workflow); var row = Row(); row.IsChecked = true; picker.Items.Add(row);
        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        Assert.IsTrue(picker.CreateDraftsCommand.CanExecute(null));
        var notifications = 0; picker.CreateDraftsCommand.CanExecuteChanged += (_, _) => notifications++;
        user.SetCurrentUser(new() { UserId = "draft-user", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit] });
        Assert.IsTrue(notifications > 0); Assert.IsFalse(picker.CreateDraftsCommand.CanExecute(null));
        await picker.CreateDraftsCommand.ExecuteAsync(null); Assert.HasCount(0, api.Commands);
        notifications = 0;
        user.SetCurrentUser(new() { UserId = "draft-user", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderCreate] });
        Assert.IsTrue(notifications > 0); Assert.IsTrue(picker.CreateDraftsCommand.CanExecute(null));
        await picker.CreateDraftsCommand.ExecuteAsync(null); Assert.HasCount(1, api.Commands);
    });

    /// <summary>撤销编辑权限时外层追加按钮同步禁用，保留追加意图；明确改新建后恢复可执行。</summary>
    [TestMethod]
    public void PurchaseInput_OuterAppendRequiresEdit() => TestInfrastructure.StaTest.RunAsync(async () =>
    {
        var api = new DraftApi { AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok(
            [new() { PurchaseOrderId = Guid.NewGuid(), Version = 1, OrderNo = "PO-APPEND" }])) };
        var user = new CurrentUserStub(true); using var workflow = Create(api, user: user); workflow.BeginSession();
        using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        picker.AttachWorkflow(workflow); var row = Row(); row.IsChecked = true; picker.Items.Add(row);
        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        var target = workflow.Targets.Single(); target.SelectedChoice = target.Choices[1];
        Assert.IsTrue(picker.CreateDraftsCommand.CanExecute(null));
        var notifications = 0; picker.CreateDraftsCommand.CanExecuteChanged += (_, _) => notifications++;
        user.SetCurrentUser(new() { UserId = "draft-user", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderCreate] });
        Assert.IsTrue(notifications > 0); Assert.IsFalse(picker.CreateDraftsCommand.CanExecute(null));
        await picker.CreateDraftsCommand.ExecuteAsync(null); Assert.HasCount(0, api.Commands);
        Assert.IsFalse(target.IsCreateNew);
        target.SelectedChoice = target.Choices[0];
        Assert.IsTrue(picker.CreateDraftsCommand.CanExecute(null));
    });

    /// <summary>追加候选与本地提交都要求读取、创建、编辑三项权限，新建只沿用创建权限。</summary>
    [TestMethod]
    [DataRow(false, false, false)][DataRow(false, false, true)]
    [DataRow(false, true, false)][DataRow(false, true, true)]
    [DataRow(true, false, false)][DataRow(true, false, true)]
    [DataRow(true, true, false)][DataRow(true, true, true)]
    public async Task PurchaseInput_AppendPermissionMatrix(bool read, bool create, bool edit)
    {
        var api = new DraftApi { AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok(
            [new() { PurchaseOrderId = Guid.NewGuid(), Version = 1, OrderNo = "PO-APPEND" }])) };
        var user = new CurrentUserStub(read) { CreateAllowed = create, EditAllowed = edit };
        using var vm = Create(api, user: user); await Open(vm, Row());
        var allowed = read && create && edit; var target = vm.Targets.Single();
        Assert.AreEqual(allowed ? 1 : 0, api.AppendCalls);
        Assert.AreEqual(allowed ? 2 : 1, target.Choices.Count);
        Assert.AreEqual(create, vm.CreateCommand.CanExecute(null));
        target.SelectedChoice = new() { TargetMode = PurchaseDraftTargetModeEnum.AppendExisting, ExistingPurchaseOrderId = Guid.NewGuid(), ExpectedPurchaseOrderVersion = 1 };
        Assert.AreEqual(allowed, vm.HasValidPreparation);
        Assert.AreEqual(allowed, vm.CreateCommand.CanExecute(null));
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.HasCount(allowed ? 1 : 0, api.Commands);
    }

    /// <summary>已有追加选择撤销编辑权限后不能新提交或重试，恢复权限仍重放未知原请求。</summary>
    [TestMethod]
    public async Task PurchaseInput_RevokedEditFreezesAppendRetry()
    {
        var api = new DraftApi { AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok(
            [new() { PurchaseOrderId = Guid.NewGuid(), Version = 1, OrderNo = "PO-APPEND" }])) };
        var user = new CurrentUserStub(true); using var vm = Create(api, user: user); await Open(vm, Row());
        vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[1];
        user.EditAllowed = false;
        Assert.IsFalse(vm.HasValidPreparation); Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[0]; Assert.IsTrue(vm.CreateCommand.CanExecute(null));
        user.EditAllowed = true; vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[1];
        api.Handler = _ => Task.FromResult(ServiceResult<CreatePurchaseDraftsResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "待核实"));
        await vm.CreateCommand.ExecuteAsync(null); var frozen = System.Text.Json.JsonSerializer.Serialize(api.Commands.Single());
        user.EditAllowed = false; await vm.CreateCommand.ExecuteAsync(null);
        Assert.HasCount(1, api.Commands); Assert.IsTrue(vm.HasUnknownCreateResult);
        user.EditAllowed = true; api.Handler = command => Task.FromResult(Success(command));
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(frozen, System.Text.Json.JsonSerializer.Serialize(api.Commands.Last()));
    }

    /// <summary>候选行提示、选择校验及直接准备入口使用相同数量两价规则，不静默舍入非法输入。</summary>
    [TestMethod]
    [DataRow("0", "0", "0", false)]
    [DataRow("-1", "0", "0", false)]
    [DataRow("1.00001", "0", "0", false)]
    [DataRow("100000000000000", "0", "0", false)]
    [DataRow("1", "-0.0001", "0", false)]
    [DataRow("1", "0", "-0.0001", false)]
    [DataRow("1", "1.00001", "0", false)]
    [DataRow("1", "0", "1.00001", false)]
    [DataRow("1", "100000000000000", "0", false)]
    [DataRow("1", "0", "100000000000000", false)]
    [DataRow("2", "99999999999999.9999", "0", false)]
    [DataRow("2", "0", "99999999999999.9999", false)]
    [DataRow("99999999999999.9999", "1", "0", true)]
    [DataRow("1", "99999999999999.9999", "0", true)]
    [DataRow("0.0001", "0.0001", "0", true)]
    [DataRow("1.2345", "2.3456", "3.4567", true)]
    [DataRow("1", "0", "0", true)]
    public void PurchaseInput_NumericBoundaries(string quantity, string unitPrice, string taxPrice, bool valid) => TestInfrastructure.StaTest.RunAsync(async () =>
    {
        var q = decimal.Parse(quantity, CultureInfo.InvariantCulture); var u = decimal.Parse(unitPrice, CultureInfo.InvariantCulture); var t = decimal.Parse(taxPrice, CultureInfo.InvariantCulture);
        var row = new PurchaseCandidateItemModel { Uid = Guid.NewGuid(), Version = 1, SourceType = "SO", SourceUid = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(), RemainingQty = decimal.MaxValue, PurchaseQty = q, UnitPrice = u, TaxPrice = t, IsChecked = true };
        Assert.AreEqual(!valid, row.HasErrors);
        var api = new DraftApi(); using var workflow = Create(api); await Open(workflow, row);
        Assert.AreEqual(valid, workflow.HasValidPreparation); Assert.AreEqual(valid, workflow.CreateCommand.CanExecute(null));
        using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner()); picker.AttachWorkflow(workflow); picker.Items.Add(row);
        await picker.ValidateSelectionCommand.ExecuteAsync(null);
        Assert.AreEqual(valid, picker.HasValidatedSelection);
        Assert.AreEqual(q, row.PurchaseQty); Assert.AreEqual(u, row.UnitPrice); Assert.AreEqual(t, row.TaxPrice);
    });

    /// <summary>两价错误变化立即通知软验证；改正后提示清除，不要求重新加载行。</summary>
    [TestMethod]
    public void PurchaseInput_PriceValidationNotifiesAndClears()
    {
        var row = Row(); var errors = 0; row.ErrorsChanged += (_, _) => errors++;
        row.UnitPrice = -1; Assert.IsTrue(row.HasErrors); Assert.IsTrue(errors > 0);
        row.UnitPrice = 0; row.TaxPrice = 0; Assert.IsFalse(row.HasErrors);
        row.TaxPrice = 0.00001m; Assert.IsTrue(row.HasErrors);
        row.TaxPrice = 0.0001m; Assert.IsFalse(row.HasErrors);
    }

    /// <summary>实际数量及两价列显示和原生编辑器都保留四位，提交编辑后输入值不变。</summary>
    [TestMethod]
    public void PurchaseInput_ActualNumericBindingsPreserveFourDecimals() => Controls.DatePickerAssistTests.RunWithApplication(async () =>
    {
        TestInfrastructure.ApplicationResourceLoader.Load(); var api = new DraftApi(); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        var row = Row(); row.PurchaseQty = row.UnitPrice = row.TaxPrice = 1.2345m; picker.Items.Add(row);
        var view = new PurchaseCandidateSelectionView { DataContext = picker };
        var window = new Window { Content = view, Width = 1600, Height = 900, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); var grid = (DataGrid)view.FindName("CandidateGrid");
            foreach (var field in new[] { nameof(row.PurchaseQty), nameof(row.UnitPrice), nameof(row.TaxPrice) })
            {
                var column = grid.Columns.OfType<DataGridTextColumn>().Single(x => (x.Binding as Binding)?.Path.Path == field);
                grid.ScrollIntoView(row, column); await Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var display = (TextBlock)column.GetCellContent(row); Assert.AreEqual(1.2345m, decimal.Parse(display.Text, display.Language.GetEquivalentCulture()));
                grid.CurrentCell = new(row, column); grid.Focus(); Assert.IsTrue(grid.BeginEdit());
                await Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var editor = (TextBox)column.GetCellContent(row); Assert.AreEqual(1.2345m, decimal.Parse(editor.Text, editor.Language.GetEquivalentCulture()));
                editor.Text = 2.3456m.ToString(editor.Language.GetEquivalentCulture()); editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
            }
            Assert.AreEqual(2.3456m, row.PurchaseQty); Assert.AreEqual(2.3456m, row.UnitPrice); Assert.AreEqual(2.3456m, row.TaxPrice);
        }
        finally { window.Close(); }
    });
}

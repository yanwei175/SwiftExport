using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Authentication;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

/// <summary>验证销售行的页面批量命令维护业务联动、写入许可与列表基线。</summary>
[TestClass]
public sealed class SalesOrderLineReorderTests
{
    /// <summary>填充软输入沿模型重算金额，标准行单位不被覆盖，权限和只读状态变化立即阻止写入。</summary>
    [TestMethod]
    public void FillDown_ShouldRecalculateAmountsRespectUnitRulesAndCurrentPermission()
    {
        var vm = CreateViewModel();
        vm.Lines.LoadFromDtos([
            new SalesOrderDetailLineDto { Uid = Guid.NewGuid(), Qty = 2, UsdPrice = 3, UnitSnapshot = "BOX" },
            new SalesOrderDetailLineDto { Uid = Guid.NewGuid(), Qty = 1, UsdPrice = 1, UnitSnapshot = "PCS" },
            new SalesOrderDetailLineDto { Uid = Guid.NewGuid(), Qty = 1, UsdPrice = 1, UnitSnapshot = "PCS", IsNonStandard = true }]);
        var source = vm.Lines.Items[0]; var standard = vm.Lines.Items[1]; var custom = vm.Lines.Items[2];
        var notifications = new List<string?>();
        standard.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var request = new FillDownRequest(source, [standard, custom],
            [nameof(SalesOrderLineModel.QtyText), nameof(SalesOrderLineModel.UsdPriceText), nameof(SalesOrderLineModel.UnitSnapshot)]);
        Assert.IsTrue(vm.FillDownCommand.CanExecute(request));
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual(2m, standard.Qty); Assert.AreEqual(6m, standard.UsdAmount);
        Assert.IsTrue(notifications.Contains(nameof(SalesOrderLineModel.UsdAmount)));
        Assert.AreEqual("PCS", standard.UnitSnapshot); Assert.AreEqual("BOX", custom.UnitSnapshot);
        Assert.IsTrue(standard.IsDirty); Assert.IsTrue(custom.IsDirty);
        source.QtyText = "8";
        standard.IsReadOnly = true;
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual(2m, standard.Qty); Assert.AreEqual(8m, custom.Qty);
        SetReadOnly(vm, true);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(request));
        source.QtyText = "10";
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual(8m, custom.Qty);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(new FillDownRequest(source, [custom], [nameof(SalesOrderLineModel.LineNumber)])));
    }

    [TestMethod]
    public void BaseListMove_ShouldSynchronizeVisibleAndAllOrderAndRejectToBaseline()
    {
        var list = CreateLoadedList("A", "B", "C");
        var first = list.Items[0];
        var second = list.Items[1];
        var third = list.Items[2];

        list.Remove(second);
        Assert.IsTrue(list.Move(third, 0));

        CollectionAssert.AreEqual(new[] { third, first }, list.Items.ToArray());
        CollectionAssert.AreEqual(new[] { third, first }, list.AllLines.Where(line => !line.IsDeleted).ToArray());
        Assert.IsFalse(list.Move(second, 0), "删除池中的行不得移动。");

        list.RejectChanges();

        CollectionAssert.AreEqual(new[] { first, second, third }, list.Items.ToArray());
        CollectionAssert.AreEqual(new[] { first, second, third }, list.AllLines.ToArray());
    }

    [TestMethod]
    public void BaseListAcceptChanges_ShouldAcceptMovedOrderAsNewBaseline()
    {
        var list = CreateLoadedList("A", "B", "C");
        var first = list.Items[0];
        var second = list.Items[1];
        var third = list.Items[2];

        Assert.IsTrue(list.Move(third, 0));
        list.AcceptChanges();
        Assert.IsTrue(list.Move(first, 2));
        list.RejectChanges();

        CollectionAssert.AreEqual(new[] { third, first, second }, list.Items.ToArray());
    }

    [TestMethod]
    public void AddedLineMoveAndReject_ShouldRemoveAdditionAndRestorePersistedOrder()
    {
        var list = CreateLoadedList("A", "B");
        var first = list.Items[0];
        var second = list.Items[1];
        var added = list.AddStandardLine(null);

        Assert.IsTrue(list.Move(added, 0));
        list.RejectChanges();

        CollectionAssert.AreEqual(new[] { first, second }, list.Items.ToArray());
        Assert.IsFalse(list.AllLines.Contains(added));
    }

    [TestMethod]
    public void ViewModelCommands_ShouldMoveRenumberSelectAndMarkDocumentDirty()
    {
        var viewModel = CreateViewModel();
        viewModel.AddStandardLineCommand.Execute(null);
        viewModel.AddStandardLineCommand.Execute(null);
        viewModel.AddStandardLineCommand.Execute(null);
        var first = viewModel.Lines.Items[0];
        var second = viewModel.Lines.Items[1];
        var third = viewModel.Lines.Items[2];

        Assert.IsFalse(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, -1)));
        Assert.IsTrue(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, 1)));
        Assert.IsFalse(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, third, 1)));

        viewModel.ReorderLineCommand.Execute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, 1));

        CollectionAssert.AreEqual(new[] { second, first, third }, viewModel.Lines.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, viewModel.Lines.Items.Select(line => line.LineNumber).ToArray());
        Assert.AreSame(first, viewModel.SelectedLine);
        Assert.IsTrue(viewModel.IsDirty);
        Assert.IsTrue(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, -1)));

        viewModel.SelectedLine = second;
        viewModel.DeleteSelectedLineCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { 1, 2 }, viewModel.Lines.Items.Select(line => line.LineNumber).ToArray());

        SetReadOnly(viewModel, true);
        Assert.IsFalse(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, -1)));
        Assert.IsFalse(viewModel.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(viewModel.Lines.Items, first, 1)));
    }

    [TestMethod]
    public void SaveRequest_ShouldPreserveMovedCollectionOrderAndLineNumbers()
    {
        var viewModel = CreateViewModel();
        viewModel.AddStandardLineCommand.Execute(null);
        viewModel.AddStandardLineCommand.Execute(null);
        var first = viewModel.Lines.Items[0];
        var second = viewModel.Lines.Items[1];
        first.Sku = "A";
        second.Sku = "B";

        viewModel.ReorderLineCommand.Execute(RowReorderTestRequests.Step(viewModel.Lines.Items, second, -1));
        var request = viewModel.Lines.CreateSaveRequest(viewModel.Header);

        CollectionAssert.AreEqual(new[] { second.Uid, first.Uid }, request.Lines.Select(line => line.Uid).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, request.Lines.Select(line => line.LineNumber).ToArray());
    }

    private static SalesOrderLineListModel CreateLoadedList(params string[] skus)
    {
        var list = new SalesOrderLineListModel();
        list.LoadFromDtos(skus.Select((sku, index) => new SalesOrderDetailLineDto
        {
            Uid = Guid.NewGuid(),
            LineNumber = index + 1,
            Sku = sku,
        }));
        return list;
    }

    private static SalesOrderDetailViewModel CreateViewModel()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Permissions = PermissionCatalog.All.Select(item => item.PermissionKey).ToList() });
        return new(null!, null!, null!, null!, null!, null!, new ReferenceDataCacheServiceStub(), user, new SalesOrderTestParameters());
    }

    private static void SetReadOnly(ViewModelBaseEx viewModel, bool value)
    {
        var property = typeof(ViewModelBaseEx).GetProperty(
            nameof(ViewModelBaseEx.IsReadOnly),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(property);
        property.SetValue(viewModel, value);
    }
}

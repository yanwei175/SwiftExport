using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderLineReorderTests
{
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

        Assert.IsFalse(viewModel.MoveLineUpCommand.CanExecute(first));
        Assert.IsTrue(viewModel.MoveLineDownCommand.CanExecute(first));
        Assert.IsFalse(viewModel.MoveLineDownCommand.CanExecute(third));

        viewModel.MoveLineDownCommand.Execute(first);

        CollectionAssert.AreEqual(new[] { second, first, third }, viewModel.Lines.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, viewModel.Lines.Items.Select(line => line.LineNumber).ToArray());
        Assert.AreSame(first, viewModel.SelectedLine);
        Assert.IsTrue(viewModel.IsDirty);
        Assert.IsTrue(viewModel.MoveLineUpCommand.CanExecute(first));

        viewModel.SelectedLine = second;
        viewModel.DeleteSelectedLineCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { 1, 2 }, viewModel.Lines.Items.Select(line => line.LineNumber).ToArray());

        SetReadOnly(viewModel, true);
        Assert.IsFalse(viewModel.MoveLineUpCommand.CanExecute(first));
        Assert.IsFalse(viewModel.MoveLineDownCommand.CanExecute(first));
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

        viewModel.MoveLineUpCommand.Execute(second);
        var request = viewModel.Lines.CreateSaveRequest(viewModel.Header);

        CollectionAssert.AreEqual(new[] { second.Uid, first.Uid }, request.Lines.Select(line => line.Uid).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, request.Lines.Select(line => line.LineNumber).ToArray());
    }

    private static SalesOrderLineListModel CreateLoadedList(params string[] skus)
    {
        var list = new SalesOrderLineListModel();
        list.LoadFromDtos(skus.Select((sku, index) => new SalesOrderDocumentLineDto
        {
            Uid = Guid.NewGuid(),
            LineNumber = index + 1,
            Sku = sku,
        }));
        return list;
    }

    private static SalesOrderDetailViewModel CreateViewModel() =>
        new(null!, null!, null!, null!, null!, null!, new ReferenceDataCacheServiceStub());

    private static void SetReadOnly(ViewModelBaseEx viewModel, bool value)
    {
        var property = typeof(ViewModelBaseEx).GetProperty(
            nameof(ViewModelBaseEx.IsReadOnly),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(property);
        property.SetValue(viewModel, value);
    }
}

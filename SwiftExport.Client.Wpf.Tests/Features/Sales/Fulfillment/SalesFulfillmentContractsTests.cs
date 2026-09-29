using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Navigation;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Fulfillment;

/// <summary>验证销售履约详情已接入只读查询而 Manager 仍不承担读写。</summary>
[TestClass]
public sealed class SalesFulfillmentContractsTests
{
    [TestMethod]
    public void RoutesUseSalesPermissionAndExplicitManagerDetailNames()
    {
        var routes = new NavigationRouteRegistry().Routes;
        var manager = routes.Single(item => item.Key == SalesFulfillmentManagerViewModel.RouteKey);
        var detail = routes.Single(item => item.Key == SalesFulfillmentDetailViewModel.RouteKey);

        Assert.AreEqual("Manager", manager.Group);
        Assert.AreEqual("Detail", detail.Group);
        Assert.AreEqual("Sales.Order.View", manager.RequiredPermission);
        Assert.AreEqual("Sales.Order.View", detail.RequiredPermission);
        Assert.IsFalse(manager.ViewModelType.Name.Contains("Workspace", StringComparison.Ordinal));
        Assert.IsFalse(detail.ViewModelType.Name.Contains("Workspace", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DetailUsesDedicatedReadOnlyQueryWhileManagerStaysShell()
    {
        var managerView = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Fulfillment", "Views", "SalesFulfillmentManagerView.xaml");
        var detailView = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Fulfillment", "Views", "SalesFulfillmentDetailView.xaml");
        var managerViewModel = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Fulfillment", "ViewModels", "SalesFulfillmentManagerViewModel.cs");
        var detailViewModel = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Fulfillment", "ViewModels", "SalesFulfillmentDetailViewModel.cs");

        StringAssert.Contains(managerView, "Manager 页面空壳");
        StringAssert.Contains(detailView, "SelectedLine.Deliveries");
        Assert.IsFalse(managerView.Contains("DataGrid", StringComparison.Ordinal));
        StringAssert.Contains(detailView, "DataGrid");
        Assert.IsFalse(managerViewModel.Contains("ISalesFulfillment", StringComparison.Ordinal));
        StringAssert.Contains(detailViewModel, "ISalesFulfillmentApi");
        Assert.IsFalse(managerViewModel.Contains("Contracts.DTOs", StringComparison.Ordinal));
        StringAssert.Contains(detailViewModel, "Contracts.DTOs.SalesOrders.Fulfillment");
    }

    [TestMethod]
    public void ManagerAndSalesOrderEntryUseStableDetailDocumentKey()
    {
        var salesManager = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Orders", "ViewModels", "SalesOrderManagerViewModel.cs");
        var fulfillmentDetail = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Fulfillment", "ViewModels", "SalesFulfillmentDetailViewModel.cs");

        StringAssert.Contains(salesManager, "$\"sales-fulfillment-detail:{order.Uid:N}\"");
        StringAssert.Contains(fulfillmentDetail, "IDocumentTabInitializable");
        StringAssert.Contains(fulfillmentDetail, "SalesFulfillmentDetailOpenRequest");
    }

    [TestMethod]
    public void SalesManagerAndDetailCreateNonEmptyConfirmCommandIds()
    {
        var manager = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Orders", "ViewModels", "SalesOrderManagerViewModel.cs");
        var detail = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Orders", "ViewModels", "SalesOrderDetailViewModel.cs");

        StringAssert.Contains(manager, "ConfirmSalesOrderCommand");
        StringAssert.Contains(manager, "CommandId = Guid.NewGuid()");
        StringAssert.Contains(detail, "ConfirmSalesOrderCommand");
        StringAssert.Contains(detail, "CommandId = Guid.NewGuid()");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    /// <summary>从测试输出目录向上定位仓库根目录，以读取页面和路由源码契约。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Navigation;
using System;
using System.IO;
using System.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

[TestClass]
public sealed class PurchaseOrderWorkspaceContractsTests
{
    [TestMethod]
    public void Navigation_MustExposeProcurementWorkspace()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(PurchaseOrderWorkspaceViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(PurchaseOrderWorkspaceViewModel), route.ViewModelType);
        var group = registry.MenuItems.Single(x => x.RouteKey == "procurement");
        Assert.IsTrue(group.Children.Any(x => x.RouteKey == PurchaseOrderWorkspaceViewModel.RouteKey));
        Assert.IsTrue(registry.TryGetRoute(PurchaseOrderDetailViewModel.RouteKey, out var detailRoute));
        Assert.AreEqual(typeof(PurchaseOrderDetailViewModel), detailRoute.ViewModelType);
    }

    [TestMethod]
    public void PurchaseWorkspacePreview_MustRemainSelectedRowMode()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "DemandPlanning", "NetDemand", "ViewModels", "PurchaseDraftPreviewViewModel.cs");
        StringAssert.Contains(source, "if (IsSourceOneClick)");
        StringAssert.Contains(source, "PurchaseDraftEntryModeEnum.PurchaseWorkspace => \"采购工作台获取净需求\"");
        Assert.IsFalse(source.Contains("EntryMode != PurchaseDraftEntryModeEnum.ManualSelection && SourceUid is null", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workspace_MustShowUnknownDownstreamAsNotConnected()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderWorkspaceViewModel.cs");
        StringAssert.Contains(source, "public string InboundText => \"尚未接入\"");
        StringAssert.Contains(source, "public string ShippedText => \"尚未接入\"");
        StringAssert.Contains(source, "PurchaseDraftEntryModeEnum.PurchaseWorkspace");
    }

    [TestMethod]
    public void ReadyQtyDecrease_MustRequireVisibleCorrectionReason()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderWorkspaceViewModel.cs");
        StringAssert.Contains(source, "ReportReadyQty < (line.LatestProgress?.ReadyQty ?? 0)");
        StringAssert.Contains(source, "CorrectionReason");
    }

    [TestMethod]
    public void WorkspaceAndDetail_MustCoverLegacyVisibleFields()
    {
        var workspace = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        foreach (var header in new[] { "订单状态", "采购单号", "诗亦霖采购单号", "供应商", "日期", "预计交期", "回复交期", "数量", "未发货", "已备好", "金额" })
            StringAssert.Contains(workspace, $"Header=\"{header}\"");

        var detail = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var label in new[] { "客户代码", "客户PO号", "业务员", "采购/跟单员", "进水管", "起泡器", "托盘", "内盒", "标贴", "说明书", "外箱", "Logo/标识", "特殊要求", "折扣备注", "采购文件夹", "复核人" })
            StringAssert.Contains(detail, label);
        foreach (var header in new[] { "图片", "客户型号", "物料编号", "AQ/关联型号", "供应商型号", "中文描述", "表面处理/颜色", "配置", "物料类型", "供货方式", "未收货", "已备好", "预计备妥", "送达供应商", "来源单/行" })
            StringAssert.Contains(detail, $"Header=\"{header}\"");
    }

    [TestMethod]
    public void DetailDocument_MustPreserveUnknownDownstreamSemanticsAndDocumentKey()
    {
        var viewModel = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderDetailViewModel.cs");
        StringAssert.Contains(viewModel, "IDocumentTabViewModel");
        StringAssert.Contains(viewModel, "IDocumentTabInitializable");
        StringAssert.Contains(viewModel, "UpdateLegacyHeaderFields = true");
        StringAssert.Contains(viewModel, "HasUnsavedChanges");
        var workspace = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderWorkspaceViewModel.cs");
        StringAssert.Contains(workspace, "$\"purchase-order:{order.Uid:N}\"");
        var detail = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        StringAssert.Contains(detail, "入库模块尚未接入");
        StringAssert.Contains(detail, "UsdTotalText");
        StringAssert.Contains(detail, "CartonTotalText");
        StringAssert.Contains(detail, "CbmTotalText");
    }

    [TestMethod]
    public void Workspace_MustUseHeadLineMasterDetailWithoutOpeningDrawerOnSelection()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Orders}\"");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(view, "采购行明细");
        StringAssert.Contains(view, "ResizeDirection=\"Rows\"");
        StringAssert.Contains(view, "Command=\"{Binding OpenDrawerCommand}\"");

        var viewModel = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderWorkspaceViewModel.cs");
        Assert.IsFalse(viewModel.Contains("IsDrawerOpen = order is not null", StringComparison.Ordinal));
        StringAssert.Contains(viewModel, "private void OpenDrawer() => IsDrawerOpen = true;");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}

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

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}

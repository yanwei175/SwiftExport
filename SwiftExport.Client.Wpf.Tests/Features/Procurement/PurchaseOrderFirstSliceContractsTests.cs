using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购执行第一切片页面职责与专用契约门禁。</summary>
[TestClass]
public sealed class PurchaseOrderFirstSliceContractsTests
{
    [TestMethod]
    public void ManagerMustRemainReadOnlyMasterDetailAndUseAuthoritativeQuickCommands()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Orders}\"");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(view, "ResizeDirection=\"Rows\"");
        StringAssert.Contains(view, "OpenDetailCommand");
        StringAssert.Contains(view, "GetFromNetDemandCommand");
        Assert.IsFalse(view.Contains("SaveCommand", StringComparison.Ordinal));
        StringAssert.Contains(view, "ConfirmCommand");
        StringAssert.Contains(view, "CancelConfirmationCommand");
        Assert.IsFalse(view.Contains("ReportProgressCommand", StringComparison.Ordinal));
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderManagerViewModel.cs");
        StringAssert.Contains(source, "GetConfirmationContextAsync(order.PurchaseOrderId)");
        StringAssert.Contains(source, "ConfirmAsync(order.PurchaseOrderId");
        StringAssert.Contains(source, "取消采购确认暂未开放");
        Assert.IsFalse(source.Contains("_orderApi.CancelConfirmationAsync", StringComparison.Ordinal));
        StringAssert.Contains(source, "requiresConsumptionBinding");
    }

    [TestMethod]
    public void DetailMustUseNarrowNewProcurementContracts()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderDetailViewModel.cs");
        StringAssert.Contains(source, "IProcurementOrderApi");
        StringAssert.Contains(source, "CreateIndependentPurchaseOrderCommand");
        StringAssert.Contains(source, "UpdatePurchaseOrderDraftCommand");
        StringAssert.Contains(source, "ConfirmProcurementOrderCommand");
        StringAssert.Contains(source, "取消采购确认暂未开放");
        Assert.IsFalse(source.Contains("_api.CancelConfirmationAsync", StringComparison.Ordinal));
        StringAssert.Contains(source, "GetConfirmationContextAsync");
        StringAssert.Contains(source, "ProcurementConfirmationBindingModel");
        Assert.IsFalse(source.Contains("IPurchaseOrderWorkspaceApi", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("LoadLegacyAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ConfirmPurchaseOrderCommand", StringComparison.Ordinal));
        StringAssert.Contains(source, "PurchaseOrderDetailDto");
    }

    [TestMethod]
    public void DetailMustExposeFixedIdentityAndSeparateReceiptFromConsumptionTargets()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var text in new[] { "客户", "实际供应商", "产品单位", "数量", "收货目标", "送至供应商", "确认消耗绑定", "BOM 路径", "消耗仓库" })
            StringAssert.Contains(view, text);
        StringAssert.Contains(view, "IsReadOnly=\"True\"");
    }

    [TestMethod]
    public void IndependentOrdersParallelPageMustNotReturn()
    {
        var root = Root();
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "SwiftExport.Client.Wpf", "Features", "Procurement", "IndependentOrders")));
        var shell = Read("SwiftExport.Client.Wpf", "Shell", "ShellWindow.xaml");
        Assert.IsFalse(shell.Contains("IndependentPurchase", StringComparison.Ordinal));
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

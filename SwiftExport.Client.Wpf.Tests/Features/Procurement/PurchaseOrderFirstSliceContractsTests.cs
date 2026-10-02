using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购页面职责与当前专用契约门禁，不再要求旧确认消耗绑定流程。</summary>
[TestClass]
public sealed class PurchaseOrderFirstSliceContractsTests
{
    /// <summary>管理页保持只读主从查询，确认和取消确认使用当前业务命令。</summary>
    [TestMethod]
    public void ManagerMustRemainReadOnlyMasterDetailAndUseAuthoritativeQuickCommands()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Orders}\"");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(view, "SwiftExportGridSplitterHorizontal");
        StringAssert.Contains(view, "OnOrdersPreviewMouseDoubleClick");
        var codeBehind = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml.cs");
        StringAssert.Contains(codeBehind, "viewModel.OpenDetailCommand.Execute(null)");
        StringAssert.Contains(view, "GetFromNetDemandCommand");
        Assert.IsFalse(view.Contains("SaveCommand", StringComparison.Ordinal));
        StringAssert.Contains(view, "ConfirmCommand");
        StringAssert.Contains(view, "CancelConfirmationCommand");
        Assert.IsFalse(view.Contains("ReportProgressCommand", StringComparison.Ordinal));
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderManagerViewModel.cs");
        StringAssert.Contains(source, "GetConfirmationContextAsync(order.PurchaseOrderId)");
        StringAssert.Contains(source, "ConfirmAsync(order.PurchaseOrderId");
        StringAssert.Contains(source, "StartReversalAsync(ProcurementReversalOperation.CancelConfirmation)");
        Assert.IsFalse(source.Contains("_orderApi.CancelConfirmationAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("requiresConsumptionBinding", StringComparison.Ordinal));
    }

    /// <summary>详情使用当前读写契约，取消确认走独立反向会话，不恢复旧消费绑定。</summary>
    [TestMethod]
    public void DetailMustUseNarrowNewProcurementContracts()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderDetailViewModel.cs");
        StringAssert.Contains(source, "IProcurementOrderApi");
        StringAssert.Contains(source, "CreateIndependentPurchaseOrderCommand");
        StringAssert.Contains(source, "UpdatePurchaseOrderDraftCommand");
        StringAssert.Contains(source, "ConfirmProcurementOrderCommand");
        StringAssert.Contains(source, "StartReversalAsync(ProcurementReversalOperation.CancelConfirmation)");
        Assert.IsFalse(source.Contains("_api.CancelConfirmationAsync", StringComparison.Ordinal));
        StringAssert.Contains(source, "GetConfirmationContextAsync");
        Assert.IsFalse(source.Contains("ProcurementConfirmationBindingModel", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IPurchaseOrderWorkspaceApi", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("LoadLegacyAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ConfirmPurchaseOrderCommand", StringComparison.Ordinal));
        StringAssert.Contains(source, "PurchaseOrderDetailDto");
    }

    /// <summary>采购固定身份与送达供应商继续可见，实际入库仓和消耗仓不在确认页绑定。</summary>
    [TestMethod]
    public void DetailMustExposeFixedIdentityAndDeliverySupplierWithoutWarehouseBinding()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var text in new[] { "客户", "实际供应商", "产品单位", "数量", "送至供应商" })
            StringAssert.Contains(view, text);
        foreach (var removed in new[] { "收货目标", "确认消耗绑定", "BOM 路径", "消耗仓库" })
            Assert.IsFalse(view.Contains(removed, StringComparison.Ordinal));
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

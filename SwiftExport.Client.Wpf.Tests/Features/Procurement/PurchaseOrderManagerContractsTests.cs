using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购工作台与详情页面契约测试；沿用原文件并按能力地图迁移已废弃的旧写职责断言。</summary>
[TestClass]
public sealed class PurchaseOrderManagerContractsTests
{
    [TestMethod]
    public void ManagerMustPreserveToolbarMasterDetailAndPagingSkeletonWithoutDrawer()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        foreach (var token in new[] { "WrapPanel", "ItemsSource=\"{Binding Orders}\"", "ItemsSource=\"{Binding Lines}\"",
                     "GridSplitter", "ResizeDirection=\"Rows\"", "PageNavigator", "新建采购单", "采购跟进" })
            StringAssert.Contains(view, token);
        Assert.IsFalse(view.Contains("RightDrawerHost", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ManagerMustUseNewChainOnlyAndExposeTwoMultiSelectStatusFilters()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderManagerViewModel.cs");
        Assert.AreEqual(2, view.Split("<controls:DropDownCheckBox", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(view, "SelectedConfirmationStatuses");
        StringAssert.Contains(view, "SelectedLifecycleStatuses");
        foreach (var forbidden in new[] { "SelectedDataScope", "DataScopes", "安排状态", "ArrangementStatus", "IPurchaseOrderWorkspaceApi", "FromLegacy", "IsLegacy" })
        {
            Assert.IsFalse(view.Contains(forbidden, StringComparison.Ordinal));
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal));
        }
        foreach (var forbidden in new[] { "SaveDraftCommand", "PrepareCancelConfirmationCommand", "ReportProgressCommand" })
            Assert.IsFalse(view.Contains(forbidden, StringComparison.Ordinal));
        StringAssert.Contains(source, "IPurchaseOrderManagerApi");
        StringAssert.Contains(source, "PurchaseOrderManagerSearchRequest");
        StringAssert.Contains(source, "GetConfirmationContextAsync(order.PurchaseOrderId)");
        StringAssert.Contains(source, "取消采购确认暂未开放");
        Assert.IsFalse(source.Contains("_orderApi.CancelConfirmationAsync", StringComparison.Ordinal));
        StringAssert.Contains(source, "HandleCommandFailureAsync");
        Assert.IsFalse(source.Contains("ProcurementOrderSearchRequest", StringComparison.Ordinal));
        Assert.AreEqual(2, view.Split("DisplayMemberPath=\"DisplayText\"", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(source, "Select(static option => option.Value).ToList()");
    }

    [TestMethod]
    public void ProcurementStatusBindingsMustUseRealEnumsAndDescriptionConverter()
    {
        var managerView = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        var detailView = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        var rowModel = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Models", "PurchaseOrderManagementRowModel.cs");
        var mapster = Read("SwiftExport.Client.Wpf", "Mappings", "MapsterConfig.cs");

        StringAssert.Contains(managerView, "{Binding LifecycleStatus,Mode=OneWay,Converter={StaticResource EnumDescriptionConverter}}");
        StringAssert.Contains(managerView, "{Binding ConfirmationStatus,Mode=OneWay,Converter={StaticResource EnumDescriptionConverter}}");
        StringAssert.Contains(detailView, "{Binding Header.LifecycleStatus,Mode=OneWay,Converter={StaticResource EnumDescriptionConverter}}");
        StringAssert.Contains(detailView, "{Binding Header.ConfirmationStatus,Mode=OneWay,Converter={StaticResource EnumDescriptionConverter}}");
        StringAssert.Contains(rowModel, "PurchaseOrderLifecycleStatusEnum LifecycleStatus");
        StringAssert.Contains(rowModel, "ConfirmationStatusEnum ConfirmationStatus");
        Assert.IsFalse(rowModel.Contains("ConfirmationStatusText", StringComparison.Ordinal));
        Assert.IsFalse(rowModel.Contains("LifecycleStatusText", StringComparison.Ordinal));
        Assert.IsFalse(mapster.Contains("ConfirmationStatus.ToString()", StringComparison.Ordinal));
        Assert.IsFalse(mapster.Contains("LifecycleStatus.ToString()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ManagerColumnsMustFollowFrozenHeaderAndLineOrder()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        AssertOrdered(view, "Header=\"生命周期\"", "Header=\"采购单号\"", "Header=\"诗亦霖采购单号\"", "Header=\"客户代码\"",
            "Header=\"供应商\"", "Header=\"确认状态\"", "Header=\"采购日期\"", "Header=\"预计交期\"", "Header=\"回复交期\"",
            "Header=\"行数\"", "Header=\"已备好数量\"", "Header=\"总数量\"", "Header=\"收货进度\"", "Header=\"含税总金额\"",
            "Header=\"已反馈行数\"", "Header=\"预计完成\"");
        AssertOrdered(view, "Header=\"行\"", "Header=\"图片\"", "Header=\"客户型号\"", "Header=\"物料编号\"", "Header=\"AQ/关联型号\"",
            "Header=\"供应商型号\"", "Header=\"中文描述\"", "Header=\"表面处理\"", "Header=\"配置\"", "Header=\"物料类型\"",
            "Header=\"供货方式\"", "Header=\"数量\"", "Header=\"未收货\"", "Header=\"已备好\"", "Header=\"计划交期\"",
            "Header=\"预计完成\"", "Header=\"最近跟进\"", "Header=\"跟进人\"", "Header=\"未税价\"", "Header=\"含税价\"",
            "Header=\"未税金额\"", "Header=\"含税金额\"", "Header=\"送至供应商\"", "Header=\"来源\"", "Header=\"行备注\"");
    }

    /// <summary>批量选择只作用于当前页，并保持勾选与双击详情两个交互边界分离。</summary>
    [TestMethod]
    public void ManagerMustExposeCurrentPageSelectionAndSeparateBatchConfirmation()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml");
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderManagerViewModel.cs");
        var codeBehind = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderManagerView.xaml.cs");

        AssertOrdered(view, "<DataGridTemplateColumn Width=\"42\"", "Header=\"生命周期\"");
        StringAssert.Contains(view, "IsAllCurrentPageChecked");
        StringAssert.Contains(view, "IsChecked=\"{Binding IsChecked");
        StringAssert.Contains(view, "IsEnabled=\"{Binding DataContext.IsNotBusy");
        StringAssert.Contains(view, "Command=\"{Binding BatchConfirmCommand}\"");
        StringAssert.Contains(view, "Text=\"{Binding SelectionSummary}\"");
        StringAssert.Contains(view, "PreviewMouseDoubleClick=\"OnOrdersPreviewMouseDoubleClick\"");
        Assert.IsFalse(view.Contains("MouseBinding", StringComparison.Ordinal));
        StringAssert.Contains(codeBehind, "FindAncestor<CheckBox>(source)");
        StringAssert.Contains(source, "GetConfirmationContextAsync(order.PurchaseOrderId)");
        StringAssert.Contains(source, "var succeededIds = new HashSet<Guid>()");
        StringAssert.Contains(source, "LoadPageCoreAsync(selectedId, preserveCheckedIds)");
        StringAssert.Contains(source, "Dictionary<Guid, PendingConfirmationState>");
        StringAssert.Contains(source, "ApplyPendingConfirmationState(item, order)");
        StringAssert.Contains(source, "!HasPendingConfirmation(SelectedOrder.PurchaseOrderId)");
        StringAssert.Contains(source, "实际提交");
        StringAssert.Contains(source, "BuildBatchCountSummary");
    }

    [TestMethod]
    public void DetailMustPreserveToolbarHeaderCardLineTabsAndBottomStatusSkeleton()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var token in new[] { "Header.OrderNo", "Header.OrderStatusText", "WrapPanel", "Border", "客户", "实际供应商", "TabControl", "采购行",
                     "确认消耗绑定", "BOM 路径", "取消确认原因" })
            StringAssert.Contains(view, token);
        Assert.IsFalse(view.Contains("Header.InboundStatusText", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("Header.ShippedStatusText", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DetailMustKeepNewWriteContractsSeparateFromLegacyReadCompatibility()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderDetailViewModel.cs");
        foreach (var token in new[] { "IProcurementOrderApi", "CreateIndependentPurchaseOrderCommand", "UpdatePurchaseOrderDraftCommand",
                     "ConfirmProcurementOrderCommand", "PurchaseOrderLineListModel", "取消采购确认暂未开放" })
            StringAssert.Contains(source, token);
        Assert.IsFalse(source.Contains("_api.CancelConfirmationAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("LoadLegacyAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IsLegacyMode", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ConfirmPurchaseOrderCommand", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DetailMustRestoreOriginalDocumentFieldsAndLineColumns()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var label in new[] { "系统单号", "来源单号", "确认状态", "确认时间", "复核人", "客户PO号", "业务员", "跟单员",
                     "进水管", "起泡器", "托盘", "内盒", "标贴", "说明书", "外箱", "折扣备注", "文件地址" })
            StringAssert.Contains(view, label);
        foreach (var header in new[] { "行号", "图片", "SKU", "客户型号", "关联型号", "供应商型号", "中文描述", "表面处理", "配置", "物料类型",
                     "供货方式", "产品单位", "数量", "未税单价", "未税总价", "计划交期", "收货目标", "送至供应商", "来源单/行", "备注" })
            StringAssert.Contains(view, $"Header=\"{header}\"");
        foreach (var tab in new[] { "采购行", "确认消耗绑定" })
            StringAssert.Contains(view, $"Header=\"{tab}\"");
        Assert.IsFalse(view.Contains("Header=\"来源/需求关联\"", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("Header=\"供应商跟进摘要\"", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("Header=\"入库记录\"", StringComparison.Ordinal));
        Assert.IsFalse(view.Contains("Header=\"业务操作记录\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CompleteEditorModelsMustRemainAndMapNewDtosExplicitly()
    {
        var header = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Models", "PurchaseOrderHeaderEditorModel.cs");
        var line = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Models", "PurchaseOrderLineEditModel.cs");
        foreach (var field in new[] { "CustomerPoNo", "SalesPerson", "PurchasePerson", "InletHoseType", "AeratorType", "PalletType", "BoxType",
                     "LabelType", "InstructionManualType", "CartonType", "LogoType", "DiscountRemark", "PurchaseOrderFolder", "ReviewedBy" })
            StringAssert.Contains(header, field);
        foreach (var field in new[] { "CustomerSku", "LinkSku", "FinishZh", "FeaturesZh", "MaterialType", "SupplyType", "MainImage",
                     "UnitSnapshot", "SourceSummary" })
            StringAssert.Contains(line, field);
        StringAssert.Contains(header, "Load(PurchaseOrderDetailDto dto)"); StringAssert.Contains(line, "BaseLineModel<PurchaseOrderLineDetailDto>");
    }

    [TestMethod]
    public void PreviewProgressAndDocumentRoutesMustRemainRegistered()
    {
        var shell = Read("SwiftExport.Client.Wpf", "Shell", "ShellWindow.xaml");
        var routes = Read("SwiftExport.Client.Wpf", "Navigation", "NavigationRouteRegistry.cs");
        StringAssert.Contains(shell, "PurchaseOrderManagerViewModel");
        StringAssert.Contains(routes, "PurchaseOrderProgressWorkspaceViewModel.RouteKey");
        StringAssert.Contains(routes, "PurchaseOrderDetailViewModel.RouteKey");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static void AssertOrdered(string source, params string[] tokens)
    {
        var previous = -1;
        foreach (var token in tokens)
        {
            var current = source.IndexOf(token, previous + 1, StringComparison.Ordinal);
            Assert.IsGreaterThan(previous, current, $"字段顺序缺失或错误：{token}");
            previous = current;
        }
    }
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}

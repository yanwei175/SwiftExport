using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Converters;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

[TestClass]
public sealed class PurchaseOrderWorkspaceContractsTests
{
    /// <summary>验证无供应商反馈时，采购行展示明确的未知事实而不是业务零。</summary>
    [TestMethod]
    public void LineWithoutProgress_MustShowNotFollowedUp()
    {
        var line = PurchaseOrderLineEditorModel.FromDto(new PurchaseOrderLineDetailDto());

        Assert.IsFalse(line.HasProgress);
        Assert.AreEqual("尚未跟进", line.LatestArrangementText);
        Assert.AreEqual("尚未跟进", line.LatestReadyQtyText);
        Assert.AreEqual("尚未跟进", line.LatestExpectedReadyDateText);
        Assert.AreEqual("尚未跟进", line.LatestFollowUpDateText);
        Assert.AreEqual("尚未跟进", line.LatestFollowUpUserText);
    }

    /// <summary>验证采购状态枚举使用统一 Description 机制显示中文。</summary>
    [TestMethod]
    public void ProcurementEnums_MustDisplayChineseDescriptions()
    {
        var converter = new EnumDescriptionConverter();

        Assert.AreEqual("活动", converter.Convert(PurchaseOrderLifecycleStatusEnum.Active, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.AreEqual("已关闭", converter.Convert(PurchaseOrderLifecycleStatusEnum.Closed, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.AreEqual("已作废", converter.Convert(PurchaseOrderLifecycleStatusEnum.Voided, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.AreEqual("未安排", converter.Convert(SupplierArrangementStatusEnum.NotArranged, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.AreEqual("已安排", converter.Convert(SupplierArrangementStatusEnum.Arranged, typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>验证未收货文本由采购数量减正式入库数量得到，且不补无意义的小数零。</summary>
    [TestMethod]
    public void UnreceivedQuantity_MustUseNumericFactsWithoutFixedDecimals()
    {
        var line = PurchaseOrderLineEditorModel.FromDto(new PurchaseOrderLineDetailDto
        {
            Quantity = 10.125m,
            RecQty = 3.125m,
            UnreceivedQtyText = "数据库旧格式不应进入界面"
        });

        Assert.AreEqual("7 / 10.125", line.UnreceivedQtyText);
    }

    /// <summary>验证采购行摘要使用历史中的最高序号反馈，并展示后台审计信息。</summary>
    [TestMethod]
    public void LineWithProgress_MustUseHighestSequenceAsLatestSummary()
    {
        var serverTime = new DateTime(2026, 8, 28, 10, 30, 0);
        var line = PurchaseOrderLineEditorModel.FromDto(new PurchaseOrderLineDetailDto
        {
            ProgressHistory =
            [
                new SupplierProgressReportDto { Sequence = 1, ReadyQty = 3m },
                new SupplierProgressReportDto
                {
                    Sequence = 2,
                    ArrangementStatus = SupplierArrangementStatusEnum.Arranged,
                    ReadyQty = 8.5m,
                    ExpectedReadyDate = new DateTime(2026, 9, 1),
                    SupplierReportedDate = serverTime,
                    CreatedBy = "跟单员甲"
                }
            ]
        });

        Assert.IsTrue(line.HasProgress);
        Assert.AreEqual(2, line.LatestProgress?.Sequence);
        Assert.AreEqual("已安排", line.LatestArrangementText);
        Assert.AreEqual("8.5", line.LatestReadyQtyText);
        Assert.AreEqual("2026-09-01", line.LatestExpectedReadyDateText);
        Assert.AreEqual("2026-08-28 10:30", line.LatestFollowUpDateText);
        Assert.AreEqual("跟单员甲", line.LatestFollowUpUserText);
    }

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
        Assert.IsTrue(registry.TryGetRoute(PurchaseOrderProgressWorkspaceViewModel.RouteKey, out var progressRoute));
        Assert.AreEqual(typeof(PurchaseOrderProgressWorkspaceViewModel), progressRoute.ViewModelType);
        Assert.IsTrue(group.Children.Any(x => x.RouteKey == PurchaseOrderProgressWorkspaceViewModel.RouteKey));
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
        var source = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderProgressWorkspaceViewModel.cs");
        StringAssert.Contains(source, "ReportReadyQty < latest.ReadyQty");
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

    /// <summary>验证采购工作台保留主从表，并使用单一 SelectedItem 驱动采购头行联动。</summary>
    [TestMethod]
    public void Workspace_MustUseHeadLineMasterDetailWithoutOpeningDrawerOnSelection()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Orders}\"");
        StringAssert.Contains(view, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(view, "采购行明细");
        StringAssert.Contains(view, "ResizeDirection=\"Rows\"");
        StringAssert.Contains(view, "Command=\"{Binding OpenDrawerCommand}\"");
        Assert.IsFalse(view.Contains("GridSelection.CurrentRow", StringComparison.Ordinal));
        StringAssert.Contains(view, "Text=\"{Binding DetailStatusText}\"");

        var viewModel = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderWorkspaceViewModel.cs");
        Assert.IsFalse(viewModel.Contains("IsDrawerOpen = order is not null", StringComparison.Ordinal));
        StringAssert.Contains(viewModel, "private void OpenDrawer() => IsDrawerOpen = true;");
    }

    /// <summary>验证状态筛选、多行工具栏和取消确认抽屉入口使用已冻结的绑定契约。</summary>
    [TestMethod]
    public void Workspace_MustUseMultiSelectFiltersAndDrawerCancellationSubmit()
    {
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        Assert.AreEqual(3, view.Split("<controls:DropDownCheckBox", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(view, "SelectedConfirmationStatuses,Mode=OneWay");
        StringAssert.Contains(view, "SelectedLifecycleStatuses,Mode=OneWay");
        StringAssert.Contains(view, "SelectedArrangementStatuses,Mode=OneWay");
        StringAssert.Contains(view, "Command=\"{Binding PrepareCancelConfirmationCommand}\"");
        StringAssert.Contains(view, "Content=\"提交取消确认\"");
        StringAssert.Contains(view, "RowDefinitions=\"Auto,4,Auto,4,Auto\"");
    }

    [TestMethod]
    public void DetailReadOnlyFieldsAndQuickDrawerClose_MustUseSafeBindings()
    {
        var detail = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        foreach (var property in new[] { "OrderNo", "SourceNo", "CustomerCode", "SupplierName", "Currency", "OrderDate", "ExpectedDate", "PurchaseOrderFolder", "ReviewedBy" })
            StringAssert.Contains(detail, $"Header.{property},Mode=OneWay");

        var workspace = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        StringAssert.Contains(workspace, "Content=\"关闭\"");
        StringAssert.Contains(workspace, "Command=\"{Binding CloseDrawerCommand}\"");
    }

    /// <summary>验证反馈登记集中到独立跟进页，原工作台与详情仅保留最新反馈摘要。</summary>
    [TestMethod]
    public void ProgressEntry_MustUseServerAuditAndCompleteFollowUpBindings()
    {
        var workspaceView = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderWorkspaceView.xaml");
        var detailView = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderDetailView.xaml");
        var progressView = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderProgressWorkspaceView.xaml");
        var progressViewModel = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "ViewModels", "PurchaseOrderProgressWorkspaceViewModel.cs");

        Assert.IsFalse(progressView.Contains("SelectedDate=\"{Binding ReportedDate", StringComparison.Ordinal));
        StringAssert.Contains(progressView, "跟进时间和跟单员由服务器自动记录");
        StringAssert.Contains(progressView, "IsProgressCorrection");
        StringAssert.Contains(progressView, "CreatedBy");
        StringAssert.Contains(progressView, "IsCorrection");
        StringAssert.Contains(progressView, "CorrectionReason");

        Assert.IsFalse(progressViewModel.Contains("SupplierReportedDate =", StringComparison.Ordinal));
        StringAssert.Contains(progressViewModel, "ReportReadyQty < 0 || ReportReadyQty > line.Quantity");
        StringAssert.Contains(progressViewModel, "RunWriteAsync");
        StringAssert.Contains(progressViewModel, "LoadPageAsync(order.Uid, line.Uid)");
        StringAssert.Contains(progressViewModel, "ReferenceEquals(_detailCancellation, cts)");

        foreach (var readOnlyView in new[] { workspaceView, detailView })
        {
            Assert.IsFalse(readOnlyView.Contains("ReportProgressCommand", StringComparison.Ordinal));
            Assert.IsFalse(readOnlyView.Contains("SelectedLine.ProgressHistory", StringComparison.Ordinal));
            StringAssert.Contains(readOnlyView, "LatestReadyQtyText");
            StringAssert.Contains(readOnlyView, "LatestFollowUpDateText");
        }
    }

    /// <summary>验证独立跟进页接入 Shell、DI，并使用精简的数量与跟进列。</summary>
    [TestMethod]
    public void ProgressWorkspace_MustBeRegisteredAndUseFocusedColumns()
    {
        var shell = Read("SwiftExport.Client.Wpf", "Shell", "ShellWindow.xaml");
        var registration = Read("SwiftExport.Client.Wpf", "Bootstrapper", "ViewRegistrationExtensions.cs");
        var view = Read("SwiftExport.Client.Wpf", "Features", "Procurement", "PurchaseOrders", "Views", "PurchaseOrderProgressWorkspaceView.xaml");

        StringAssert.Contains(shell, "PurchaseOrderProgressWorkspaceViewModel");
        StringAssert.Contains(shell, "PurchaseOrderProgressWorkspaceView");
        StringAssert.Contains(registration, "AddTransient<PurchaseOrderProgressWorkspaceViewModel>()");
        foreach (var header in new[] { "采购单号", "供应商", "采购总量", "采购数量", "已入库", "未收货", "供应商安排", "累计已备好", "预计备妥", "最近跟进", "跟单员" })
            StringAssert.Contains(view, $"Header=\"{header}\"");
        StringAssert.Contains(view, "Header=\"已跟进行数\"");
        StringAssert.Contains(view, "Header=\"最新累计已备好合计\"");
        StringAssert.Contains(view, "LifecycleStatus,Converter={StaticResource EnumDescriptionConverter}");
        Assert.IsFalse(view.Contains("Header=\"金额\"", StringComparison.Ordinal));
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

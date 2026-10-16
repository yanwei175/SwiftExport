using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Components;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购详情专用 Lookup、产品回填、客供件目标与创建行顺序的回归测试。</summary>
[TestClass]
public sealed class PurchaseOrderLookupAndReorderTests
{
    /// <summary>创建态命令维护真实集合及行号；已保存行、权限撤销、只读及释放后均不能绕过门禁。</summary>
    [TestMethod]
    public void DragReorderOnlyChangesNewCreateRows() => StaTest.RunAsync(async () =>
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.ProcurementOrderCreate] });
        using var vm = new PurchaseOrderDetailViewModel(null!, null!, null!, null!, null!, user);
        await vm.InitializeDocumentAsync(null, default);
        var first = vm.Lines.Items[0];
        var second = vm.Lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = 10, Quantity = 7m });
        var request = new RowReorderRequest(second, first, RowDropPosition.Before);
        Assert.IsTrue(vm.ReorderLineCommand.CanExecute(request));
        vm.ReorderLineCommand.Execute(request);
        Assert.AreSame(second, vm.Lines.Items[0]);
        Assert.AreSame(second, vm.Lines.SelectedItem);
        CollectionAssert.AreEqual(new[] { 1, 2 }, vm.Lines.Items.Select(row => row.LineNumber).ToArray());
        Assert.AreEqual(7m, second.Quantity);
        vm.IsCreateMode = false;
        var reverse = new RowReorderRequest(first, second, RowDropPosition.Before);
        Assert.IsFalse(vm.ReorderLineCommand.CanExecute(reverse));
        vm.ReorderLineCommand.Execute(reverse);
        Assert.AreSame(second, vm.Lines.Items[0]);
        vm.IsCreateMode = true;
        user.SetCurrentUser(new MeDto { Permissions = [] });
        Assert.IsFalse(vm.ReorderLineCommand.CanExecute(reverse));
        user.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.ProcurementOrderCreate] });
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(vm, true);
        Assert.IsFalse(vm.ReorderLineCommand.CanExecute(reverse));
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(vm, false);
        vm.Lines.LoadFromDtos([new() { Uid = first.Uid, LineNumber = 1, Quantity = 1m }, new() { Uid = second.Uid, LineNumber = 2, Quantity = 7m }]);
        Assert.IsFalse(vm.ReorderLineCommand.CanExecute(new RowReorderRequest(vm.Lines.Items[1], vm.Lines.Items[0], RowDropPosition.Before)));
        Assert.IsFalse(vm.ReorderLineCommand.CanExecute(reverse));
        vm.Dispose();
        Assert.IsFalse(vm.CanReorder);
    });

    [TestMethod]
    public void LookupSelectedItemsMustUseNarrowProcurementDtos()
    {
        Assert.AreEqual(typeof(ProcurementCustomerCandidateDto), PurchaseCustomerLookupTextBox.SelectedCustomerProperty.PropertyType);
        Assert.AreEqual(typeof(ProcurementSupplierCandidateDto), PurchaseSupplierLookupTextBox.SelectedSupplierProperty.PropertyType);
        Assert.AreEqual(typeof(ProcurementProductCandidateDto), PurchaseProductLookupTextBox.SelectedProductProperty.PropertyType);
    }

    [TestMethod]
    public void ProductSelectionMustFillLineAndRequireDeliverySupplierForCustomerSuppliedMaterial()
    {
        var lines = new PurchaseOrderLineListModel();
        var line = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Quantity = 1m });
        line.ApplyProduct(new ProcurementProductCandidateDto
        {
            Uid = Guid.NewGuid(), Sku = "SKU-1", MainImage = "main.png", CustomerSku = "C-1", LinkSku = "AQ-1",
            SupplierSku = "S-1", DescriptionZh = "描述", FinishZh = "亮铬", FeaturesZh = "配置",
            MaterialType = MaterialTypesEnum.客供件, SupplyType = SupplyTypeEnums.采购, Unit = "PCS",
            UnitPrice = 12.5m, TaxPrice = 14.1m
        });

        Assert.AreEqual("C-1", line.CustomerSku);
        Assert.AreEqual("S-1", line.SupplierSku);
        Assert.AreEqual("AQ-1", line.LinkSku);
        Assert.AreEqual("main.png", line.MainImage);
        Assert.AreEqual(12.5m, line.UnitPrice);
        Assert.AreEqual(14.1m, line.TaxPrice);
        Assert.IsTrue(line.RequiresDeliverySupplier);
        StringAssert.Contains(line.ValidateRow(), "送达供应商");

        line.DeliverySupplier = new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "SUP", SupplierName = "供应商" };
        Assert.AreEqual("供应商", line.DeliverySupplierName);
        Assert.IsNull(line.ValidateRow(), "客供件明确送达供应商即可通过行校验，不要求采购收货仓。");
    }

    [TestMethod]
    public void NonCustomerSuppliedProductMustClearStaleDeliverySupplier()
    {
        var lines = new PurchaseOrderLineListModel();
        var line = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Quantity = 1m });
        line.DeliverySupplier = new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "OLD", SupplierName = "旧目标" };
        line.ApplyProduct(new ProcurementProductCandidateDto
        { Uid = Guid.NewGuid(), Sku = "SKU-2", MaterialType = MaterialTypesEnum.成品, SupplyType = SupplyTypeEnums.采购, Unit = "PCS" });

        Assert.IsFalse(line.RequiresDeliverySupplier);
        Assert.IsNull(line.DeliverySupplierId);
        Assert.AreEqual(string.Empty, line.DeliverySupplierName);
    }

    /// <summary>已保存采购行拒绝通过 Lookup 改变产品身份。</summary>
    [TestMethod]
    public void PersistedLineMustIgnoreProductLookupCommit()
    {
        var originalProductId = Guid.NewGuid();
        var lines = new PurchaseOrderLineListModel();
        lines.LoadFromDtos(new[]
        {
            new PurchaseOrderLineDetailDto
            {
                Uid = Guid.NewGuid(), ProductId = originalProductId, Sku = "OLD", Quantity = 1m,
                UnitSnapshot = "PCS"
            }
        });

        lines.Items[0].ApplyProduct(new ProcurementProductCandidateDto
        { Uid = Guid.NewGuid(), Sku = "NEW", SupplyType = SupplyTypeEnums.采购 });

        Assert.AreEqual(originalProductId, lines.Items[0].ProductId);
        Assert.AreEqual("OLD", lines.Items[0].Sku);
    }

    /// <summary>采购 SKU 编辑器与销售行一致，先暂存候选，再把当前行传给显式提交命令。</summary>
    [TestMethod]
    public void ProductLookupMustUsePageCandidateAndExplicitCommit()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/Views/PurchaseOrderDetailView.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderDetailViewModel.cs"));

        StringAssert.Contains(xaml, "DataContext.SelectedProductCandidate");
        StringAssert.Contains(xaml, "Mode=OneWayToSource");
        StringAssert.Contains(xaml, "SelectionCommittedCommandParameter=\"{Binding}\"");
        StringAssert.Contains(viewModel, "line.ApplyProduct(SelectedProductCandidate)");
        StringAssert.Contains(viewModel, "SelectedProductCandidate = null");
        StringAssert.Contains(viewModel, "ProductCandidates.Clear()");
    }

    [TestMethod]
    public void MoveMustKeepSelectedLineAndSupportContinuousRenumbering()
    {
        var lines = new PurchaseOrderLineListModel();
        var first = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = 1, Quantity = 1m });
        var second = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = 2, Quantity = 1m });
        lines.SelectedItem = second;

        Assert.IsTrue(lines.Move(second, 0));
        for (var index = 0; index < lines.Items.Count; index++) lines.Items[index].LineNumber = index + 1;

        Assert.AreSame(second, lines.SelectedItem);
        Assert.AreEqual(1, second.LineNumber);
        Assert.AreEqual(2, first.LineNumber);
    }

    /// <summary>采购行表保留原生列与安全重排条件，不挂接无错误来源的软验证。</summary>
    [TestMethod]
    public void PurchaseLineGridMustKeepNativeColumnsAndSafeReorder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/Views/PurchaseOrderDetailView.xaml"));

        StringAssert.Contains(xaml, "CanUserSortColumns=\"False\"");
        Assert.IsFalse(xaml.Contains("GridOperations", StringComparison.Ordinal));
        StringAssert.Contains(xaml, "rowDrag:RowDragReorder.IsEnabled=\"{Binding CanReorder}\"");
        StringAssert.Contains(xaml, "CurrentItem=\"{Binding Lines.SelectedItem,Mode=TwoWay}\"");
        Assert.IsFalse(xaml.Contains("GridValidation.IsEnabled", StringComparison.Ordinal));
        StringAssert.Contains(xaml, "<DataTrigger Binding=\"{Binding IsCreateMode}\" Value=\"True\">");

        var expectedHeaders = new[] { "排序", "行号", "图片", "SKU", "客户型号", "关联型号", "供应商型号", "中文描述", "表面处理", "配置", "物料类型", "供货方式", "产品单位", "数量", "未税单价", "未税总价", "计划交期", "送至供应商", "来源单/行", "备注" };
        foreach (var header in expectedHeaders)
            StringAssert.Contains(xaml, $"Header=\"{header}\"");
        Assert.IsFalse(xaml.Contains("WarehouseCandidates"));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderModelsAndContractsTests
{
    [TestMethod]
    public void NonStandardSku_ShouldBeStableUppercaseAndUseSharedPrefix()
    {
        var uid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        var sku = NonStandardSkuGenerator.Create(uid);

        Assert.AreEqual($"{DocumentTypeStaticsEnum.NonStandardPart}-0123456789AB", sku);
        Assert.IsTrue(NonStandardSkuGenerator.IsGeneratedNonStandardSku(sku));
        Assert.IsFalse(NonStandardSkuGenerator.IsGeneratedNonStandardSku("非标件-anything"));
        Assert.ThrowsExactly<ArgumentException>(() => NonStandardSkuGenerator.Create(Guid.Empty));
    }

    [TestMethod]
    public void AddNonStandardLine_ShouldNotRequireProductAndShouldValidateBusinessSnapshot()
    {
        var list = new SalesOrderLineListModel();
        var deliveryDate = new DateTime(2026, 8, 1);

        var line = list.AddNonStandardLine(deliveryDate);

        Assert.IsTrue(line.IsNonStandard);
        Assert.IsNull(line.ProductId);
        Assert.AreEqual(line.ExpectedDeliveryDate, (DateTime?)deliveryDate);
        Assert.AreEqual(NonStandardSkuGenerator.Create(line.Uid), line.Sku);
        Assert.AreEqual("数量必须大于零。", line.ValidateRow());
        Assert.AreEqual("数量必须大于零。", line[nameof(SalesOrderLineModel.Qty)]);

        line.Qty = 12;
        Assert.AreEqual("非标行必须填写产品描述。", line.ValidateRow());
        Assert.AreEqual("非标行必须填写产品描述。", line[nameof(SalesOrderLineModel.DescriptionEn)]);
        line.DescriptionEn = "Temporary custom brass component";
        Assert.IsNull(line.ValidateRow());
    }

    [TestMethod]
    public void StandardLine_ShouldRequireProductLookup()
    {
        var line = new SalesOrderLineListModel().AddStandardLine(null);
        line.Qty = 1;

        Assert.AreEqual("标准行必须通过产品 Lookup 选择产品。", line.ValidateRow());

        line.ApplyProduct(new SalesOrderProductLookupDto
        {
            ProductId = Guid.NewGuid(),
            Sku = "STD-001",
            DescriptionEn = "Standard product",
        });

        Assert.IsNull(line.ValidateRow());
    }

    [TestMethod]
    public void NonStandardSaveDto_ShouldRecalculateSkuAndClearProductAssociations()
    {
        var line = new SalesOrderLineListModel().AddNonStandardLine(null);
        line.Qty = 2;
        line.DescriptionEn = "Custom item";
        line.ProductId = Guid.NewGuid();
        line.CustomerProductId = Guid.NewGuid();
        line.Sku = "client-tampered";

        var dto = line.ToEditDto();

        Assert.IsNull(dto.ProductId);
        Assert.IsNull(dto.CustomerProductId);
        Assert.AreEqual(NonStandardSkuGenerator.Create(line.Uid), dto.Sku);
    }

    [TestMethod]
    public void WorkspaceContracts_ShouldNotExposeOldSalesOrderOrApprovalStatus()
    {
        Assert.IsNull(typeof(SalesOrderEditHeaderDto).GetProperty("OrderStatus"));
        Assert.IsNull(typeof(SalesOrderEditHeaderDto).GetProperty("ApprovalStatus"));
        Assert.IsNull(typeof(SalesOrderListItemDto).GetProperty("OrderStatus"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("CreatedDate"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("UpdatedBy"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("IsDeleted"));
        Assert.IsNotNull(typeof(ISalesOrderWorkspaceApi).GetMethod(nameof(ISalesOrderWorkspaceApi.ConfirmAsync)));

        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(SalesOrderManagerViewModel.ManagerRouteKey, out var manager));
        Assert.AreEqual(typeof(SalesOrderManagerViewModel), manager.ViewModelType);
        Assert.IsTrue(registry.TryGetRoute(SalesOrderManagerViewModel.DetailRouteKey, out var detail));
        Assert.AreEqual(typeof(SalesOrderDetailViewModel), detail.ViewModelType);
    }
}

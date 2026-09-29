using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Services.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.WebApi.Controllers.Systems.ExcelFieldMappings;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>冻结 Excel 映射的共享规范化规则、业务目录和 HTTP 契约。</summary>
[TestClass]
public sealed class ExcelFieldMappingCatalogTests
{
    /// <summary>兼容宽字符、大小写和连续空白，保留非空白字符原样。</summary>
    [TestMethod]
    public void Normalizer_CollapsesEquivalentHeaders()
    {
        Assert.AreEqual("CUSTOMER SKU", ExcelHeaderNormalizer.Normalize("  Ｃｕｓｔｏｍｅｒ\t\u3000sku  "));
        Assert.AreEqual("", ExcelHeaderNormalizer.Normalize(" \t "));
        Assert.AreEqual("客户型号", ExcelHeaderNormalizer.Normalize("客户型号"));
    }

    /// <summary>仅公布已确认 DTO 可写字段，并标出采购创建与更新差异。</summary>
    [TestMethod]
    public void Catalog_ContainsThreeGroupsAndPurchaseApplicability()
    {
        CollectionAssert.AreEquivalent(new[] { "SalesOrder", "PurchaseOrder", "PackingList" },
            ExcelFieldCatalog.GetGroups().Select(group => group.GroupCode).ToArray());
        Assert.IsTrue(ExcelFieldCatalog.ContainsField("SalesOrder", "Line.CustomerSku"));
        Assert.IsTrue(ExcelFieldCatalog.ContainsField("PackingList", "Line.CustomerSkuSnapshot"));
        Assert.IsFalse(ExcelFieldCatalog.ContainsField("SalesOrder", "Line.Uid"));
        Assert.IsFalse(ExcelFieldCatalog.ContainsField("PackingList", "Line.PackageCount"));
        var purchase = ExcelFieldCatalog.GetFields("PurchaseOrder");
        var quantity = purchase.Single(field => field.FieldKey == "Line.Quantity");
        Assert.IsTrue(quantity.SupportsCreate);
        Assert.IsFalse(quantity.SupportsUpdate);
        var discount = purchase.Single(field => field.FieldKey == "Header.DiscountRemark");
        Assert.IsFalse(discount.SupportsCreate);
        Assert.IsTrue(discount.SupportsUpdate);
    }

    /// <summary>目录路径必须真实存在于适用的写入 DTO，避免后续重命名形成无效配置。</summary>
    [TestMethod]
    public void Catalog_FieldsExistOnTheirWriteDtos()
    {
        Check("SalesOrder", typeof(SalesOrderEditHeaderDto), typeof(SalesOrderEditLineDto));
        Check("PackingList", typeof(PackingListDraftHeaderInput), typeof(PackingListDraftLineInput));
        foreach (var field in ExcelFieldCatalog.GetFields("PurchaseOrder"))
        {
            if (field.SupportsCreate)
                CheckField(field.FieldKey, typeof(CreateIndependentPurchaseOrderCommand), typeof(CreateIndependentPurchaseOrderLineCommand));
            if (field.SupportsUpdate)
                CheckField(field.FieldKey, typeof(UpdatePurchaseOrderDraftCommand), typeof(UpdatePurchaseOrderDraftLineCommand));
        }
    }

    private static void Check(string group, Type headerType, Type lineType)
    {
        foreach (var field in ExcelFieldCatalog.GetFields(group)) CheckField(field.FieldKey, headerType, lineType);
    }

    private static void CheckField(string key, Type headerType, Type lineType)
    {
        var parts = key.Split('.');
        Assert.HasCount(2, parts, key);
        var target = parts[0] == "Header" ? headerType : lineType;
        Assert.IsNotNull(target.GetProperty(parts[1]), key);
    }

    /// <summary>Controller 与 Refit 共享路由，权限键在可分配目录中存在。</summary>
    [TestMethod]
    public void ApiRouteAndPermission_ArePublished()
    {
        Assert.AreEqual("api/excel-field-mappings", typeof(ExcelFieldMappingController).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.AreEqual("/api/excel-field-mappings/groups", typeof(IExcelFieldMappingApi).GetMethod("GetGroupsAsync")?.GetCustomAttribute<GetAttribute>()?.Path);
        Assert.AreEqual("/api/excel-field-mappings/{uid}", typeof(IExcelFieldMappingApi).GetMethod("DeleteAsync")?.GetCustomAttribute<DeleteAttribute>()?.Path);
        Assert.IsTrue(PermissionCatalog.All.Any(item => item.PermissionKey == PermissionKeys.SystemExcelFieldMappingManage && item.IsAssignable));
        Assert.AreEqual(409, ServiceErrorHttpMapper.GetStatusCode(ExcelFieldMappingErrorCodes.DuplicateHeader));
    }
}

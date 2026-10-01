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

/// <summary>冻结 Excel 映射的共享规范化规则、HTTP 契约及目录迁出边界。</summary>
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

    /// <summary>Controller 与 Refit 共享路由，权限键在可分配目录中存在。</summary>
    [TestMethod]
    public void ApiRouteAndPermission_ArePublished()
    {
        Assert.AreEqual("api/excel-field-mappings", typeof(ExcelFieldMappingController).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.IsNull(typeof(IExcelFieldMappingApi).GetMethod("GetGroupsAsync"));
        Assert.IsNull(typeof(IExcelFieldMappingApi).GetMethod("GetFieldsAsync"));
        Assert.IsNull(typeof(ExcelFieldMappingController).GetMethod("GetGroupsAsync"));
        Assert.IsNull(typeof(ExcelFieldMappingController).GetMethod("GetFieldsAsync"));
        Assert.AreEqual("/api/excel-field-mappings/{groupCode}", typeof(IExcelFieldMappingApi).GetMethod("GetMappingsAsync")?.GetCustomAttribute<GetAttribute>()?.Path);
        Assert.AreEqual("/api/excel-field-mappings/{uid}", typeof(IExcelFieldMappingApi).GetMethod("DeleteAsync")?.GetCustomAttribute<DeleteAttribute>()?.Path);
        Assert.AreEqual("/api/excel-field-mappings/{groupCode}/batch", typeof(IExcelFieldMappingApi).GetMethod("SaveGroupAsync")?.GetCustomAttribute<PutAttribute>()?.Path);
        Assert.IsTrue(PermissionCatalog.All.Any(item => item.PermissionKey == PermissionKeys.SystemExcelFieldMappingManage && item.IsAssignable));
        Assert.AreEqual(409, ServiceErrorHttpMapper.GetStatusCode(ExcelFieldMappingErrorCodes.DuplicateHeader));
    }
}

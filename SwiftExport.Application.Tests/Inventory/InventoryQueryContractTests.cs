using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Inventory;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Infrastructure.Repositories.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证库存查询契约保持只读、分页且受认证边界保护。</summary>
[TestClass]
public sealed class InventoryQueryContractTests
{
    /// <summary>Refit 只公开余额、流水与 Trace 三个查询，不重新暴露期初或余额写接口。</summary>
    [TestMethod]
    public void InventoryApi_ShouldExposeOnlyReadModelsAndStableRoutes()
    {
        var methods = typeof(IInventoryApi).GetMethods().ToDictionary(x => x.Name);

        Assert.AreEqual(3, methods.Count);
        Assert.AreEqual("/balances/search", methods[nameof(IInventoryApi.SearchBalancesAsync)].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/ledger/search", methods[nameof(IInventoryApi.SearchLedgerAsync)].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/balances/{uid}/trace", methods[nameof(IInventoryApi.GetTraceAsync)].GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single().Path);
        Assert.IsNotNull(typeof(InventoryBalanceTraceDto).GetProperty(nameof(InventoryBalanceTraceDto.Reconciliation)));
        Assert.AreEqual(typeof(string), typeof(InventoryBalanceDto).GetProperty(nameof(InventoryBalanceDto.ImagePath))?.PropertyType);
    }

    /// <summary>库存 HTTP 边界要求认证，细粒度模块权限继续由 Application 服务重验。</summary>
    [TestMethod]
    public void InventoryController_ShouldRequireAuthorization()
    {
        Assert.IsTrue(typeof(InventoryController).IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
    }

    /// <summary>仓库选择器保持独立 GET 查询契约，并由 Controller 显式从查询字符串绑定分页条件。</summary>
    [TestMethod]
    public void WarehousePicker_ShouldKeepRouteQueryAndDtoContract()
    {
        var apiMethod = typeof(IWarehouseSiteApi).GetMethod(nameof(IWarehouseSiteApi.GetWarehousePickerPagedAsync))!;
        Assert.AreEqual("/picker", apiMethod.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single().Path);
        Assert.IsTrue(apiMethod.GetParameters()[0].IsDefined(typeof(QueryAttribute), false));
        Assert.AreEqual(typeof(Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>), apiMethod.ReturnType);

        var controllerMethod = typeof(WarehouseSitesController).GetMethod(nameof(WarehouseSitesController.GetWarehousePickerPagedAsync))!;
        Assert.AreEqual("picker", controllerMethod.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template);
        Assert.IsTrue(controllerMethod.GetParameters()[0].IsDefined(typeof(FromQueryAttribute), false));
        Assert.IsTrue(controllerMethod.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));

        Assert.AreEqual("SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker", typeof(WarehouseSitePickerDto).Namespace);
        CollectionAssert.AreEquivalent(
            new[] { "Uid", "Version", "Code", "Name", "SupplierId", "SupplierCode", "SupplierName", "SiteType", "IsActive" },
            typeof(WarehouseSitePickerDto).GetProperties().Select(property => property.Name).ToArray());
    }

    /// <summary>仓库启用命令在 Refit 与 Controller 保持对称路由，并使用独立窄命令契约。</summary>
    [TestMethod]
    public void WarehouseActivate_ShouldKeepRouteBodyAndCommandContract()
    {
        var apiMethod = typeof(IWarehouseSiteApi).GetMethod(nameof(IWarehouseSiteApi.ActivateAsync))!;
        Assert.AreEqual("/{uid}/activate", apiMethod.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.IsTrue(apiMethod.GetParameters()[1].IsDefined(typeof(BodyAttribute), false));
        Assert.AreEqual(typeof(ActivateWarehouseSiteCommand), apiMethod.GetParameters()[1].ParameterType);

        var controllerMethod = typeof(WarehouseSitesController).GetMethod(nameof(WarehouseSitesController.ActivateAsync))!;
        Assert.AreEqual("{uid:guid}/activate", controllerMethod.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>().Single().Template);
        Assert.AreEqual(typeof(ActivateWarehouseSiteCommand), controllerMethod.GetParameters()[1].ParameterType);
        CollectionAssert.AreEquivalent(
            new[] { "CommandId", "ExpectedVersion", "Reason" },
            typeof(ActivateWarehouseSiteCommand).GetProperties().Select(property => property.Name).ToArray());
    }

    /// <summary>余额列表与详情必须使用 Supplier 的真实业务列名，避免部署后才暴露无效列错误。</summary>
    [TestMethod]
    public void InventoryRepository_ShouldUseSupplierBusinessColumns()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Repositories", "Inventory", "InventoryRepository.cs"));

        Assert.IsFalse(source.Contains("s.Code AS SupplierCode", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("s.Name AS SupplierName", StringComparison.Ordinal));
        Assert.AreEqual(2, Count(source, "s.SupplierCode,s.SupplierName"));
    }

    /// <summary>余额列表与详情仅左连接当前有效产品图片，不得改变余额 count 查询。</summary>
    [TestMethod]
    public void InventoryRepository_ShouldReadLiveProductImageWithoutFilteringBalances()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Repositories", "Inventory", "InventoryRepository.cs"));

        Assert.AreEqual(2, Count(source, "LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0"));
        Assert.AreEqual(2, Count(source, "p.MainImage AS ImagePath"));
        StringAssert.Contains(source, "SELECT COUNT(1) FROM dbo.InventoryBalance i JOIN dbo.WarehouseSite w");
    }

    /// <summary>选择器 SQL 仅返回活动未删除仓库，保留非活动供应商显示，并对 LIKE 特殊字符逐一转义。</summary>
    [TestMethod]
    public void WarehousePickerRepository_ShouldKeepFilteringFallbackAndLiteralLikeRules()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Repositories", "Inventory", "WarehouseSiteRepository.cs"));
        var pickerStart = source.IndexOf("GetPickerPagedAsync", StringComparison.Ordinal);
        var pickerEnd = source.IndexOf("SearchSupplierLookupAsync", pickerStart, StringComparison.Ordinal);
        var pickerSource = source.Substring(pickerStart, pickerEnd - pickerStart);
        StringAssert.Contains(pickerSource, "w.IsActive=1");
        StringAssert.Contains(pickerSource, "ISNULL(w.IsDeleted,0)=0");
        Assert.IsFalse(pickerSource.Contains("s.IsActive=1", StringComparison.Ordinal));
        StringAssert.Contains(pickerSource, "COALESCE(s.SupplierCode,w.SupplierCodeSnapshot,N'')");
        StringAssert.Contains(pickerSource, "COALESCE(s.SupplierName,w.SupplierNameSnapshot,N'')");
        StringAssert.Contains(pickerSource, "ORDER BY w.Code,w.Uid");

        var escape = typeof(WarehouseSiteRepository).GetMethod("EscapeLike", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.AreEqual("~%~_~[~~", escape.Invoke(null, ["%_[~"]));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}

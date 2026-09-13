using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.WebApi.Controllers.Inventory;

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
    }

    /// <summary>库存 HTTP 边界要求认证，细粒度模块权限继续由 Application 服务重验。</summary>
    [TestMethod]
    public void InventoryController_ShouldRequireAuthorization()
    {
        Assert.IsTrue(typeof(InventoryController).IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
    }
}

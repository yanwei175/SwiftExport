using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Refit;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.WebApi.Controllers.ForExcel;
namespace SwiftExport.Application.Tests.ForExcel;

/// <summary>验证 HTTP 契约和 PG 只读 SQL 结构，不替代真实 PostgreSQL 集成测试。</summary>
[TestClass]
public sealed class ForExcelSalesOrderContractTests
{
    /// <summary>Controller 与 Refit 路由、取消和认证边界保持一致。</summary>
    [TestMethod]
    public void HttpContract()
    {
        var controller=typeof(ForExcelSalesOrderController);
        Assert.AreEqual("api/for-excel/sales-orders",((RouteAttribute)Attribute.GetCustomAttribute(controller,typeof(RouteAttribute))!).Template);
        Assert.IsNotNull(Attribute.GetCustomAttribute(controller,typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)));
        var action=controller.GetMethod("CheckAsync")!;
        Assert.AreEqual("check",((HttpPostAttribute)Attribute.GetCustomAttribute(action,typeof(HttpPostAttribute))!).Template);
        var api=typeof(IForExcelSalesOrderApi).GetMethod("CheckAsync")!;
        Assert.AreEqual("/check",((PostAttribute)Attribute.GetCustomAttribute(api,typeof(PostAttribute))!).Path);
        Assert.AreEqual(typeof(CancellationToken),api.GetParameters()[1].ParameterType);
        Assert.IsNotNull(Attribute.GetCustomAttribute(api.GetParameters()[0],typeof(BodyAttribute)));
        var value=new CheckSalesOrderImportRequest {CustomerCode="C",Lines=[new(){ClientLineId=Guid.NewGuid(),IsNonStandard=true,Qty=1}]};
        var json=System.Text.Json.JsonSerializer.Serialize(value);
        var roundtrip=System.Text.Json.JsonSerializer.Deserialize<CheckSalesOrderImportRequest>(json)!;
        Assert.AreEqual(value.Lines[0].ClientLineId,roundtrip.Lines[0].ClientLineId);
        Assert.IsTrue(roundtrip.Lines[0].IsNonStandard);
    }
    /// <summary>查询限定客户、保留断链候选、使用主表 SKU，且编号匹配不引入空值误报。</summary>
    [TestMethod]
    public void SqlContract()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !Directory.Exists(Path.Combine(directory.FullName,"SwiftExport.Infrastructure")))directory=directory.Parent;
        Assert.IsNotNull(directory);
        var source=File.ReadAllText(Path.Combine(directory.FullName,"SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelSalesOrderRepository.cs"));
        StringAssert.Contains(source,"cp.customer_id=@CustomerId");StringAssert.Contains(source,"parameters.AddTexts(\"Skus\", customerSkus)");
        StringAssert.Contains(source,"= ANY(");StringAssert.Contains(source,"FROM unnest(@Skus)");
        StringAssert.Contains(source,"LEFT JOIN public.product");StringAssert.Contains(source,"p.sku AS \"Sku\"");
        StringAssert.Contains(source,"@InvoiceNo<>''");StringAssert.Contains(source,"@CustomerPoNo<>''");
        StringAssert.Contains(source,"context.Session.Transaction");StringAssert.Contains(source,"cancellationToken: token");
        Assert.IsFalse(source.Contains("NOLOCK"));Assert.IsFalse(source.Contains("SELECT *"));
        Assert.IsFalse(source.Contains("INSERT "));Assert.IsFalse(source.Contains("UPDATE "));Assert.IsFalse(source.Contains("DELETE "));
    }
}


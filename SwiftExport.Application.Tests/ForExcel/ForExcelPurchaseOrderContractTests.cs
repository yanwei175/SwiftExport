using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.WebApi.Controllers.ForExcel;
namespace SwiftExport.Application.Tests.ForExcel;

/// <summary>采购导入 HTTP、兼容指纹和 SQL 契约检查，不替代真实数据库测试。</summary>
[TestClass]
public sealed class ForExcelPurchaseOrderContractTests
{
    /// <summary>认证、HTTP 路由、Body、取消及响应类型与 Refit 完全对应。</summary>
    [TestMethod]
    public void HttpAndSerializationContract()
    {
        var controller=typeof(ForExcelPurchaseOrderController);
        Assert.AreEqual("api/for-excel/purchase-orders",controller.GetCustomAttributes(typeof(RouteAttribute),false).Cast<RouteAttribute>().Single().Template);
        Assert.IsNotNull(Attribute.GetCustomAttribute(controller,typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)));
        var action=controller.GetMethod("CheckAsync")!;var api=typeof(IForExcelPurchaseOrderApi).GetMethod("CheckAsync")!;
        Assert.AreEqual("check",Attribute.GetCustomAttribute(action,typeof(HttpPostAttribute)) is HttpPostAttribute post?post.Template:null);
        Assert.AreEqual("/check",((PostAttribute)Attribute.GetCustomAttribute(api,typeof(PostAttribute))!).Path);
        Assert.IsNotNull(Attribute.GetCustomAttribute(api.GetParameters()[0],typeof(BodyAttribute)));
        Assert.IsNotNull(Attribute.GetCustomAttribute(action.GetParameters()[0],typeof(FromBodyAttribute)));
        Assert.AreEqual(typeof(CancellationToken),api.GetParameters()[1].ParameterType);
        Assert.AreEqual(action.ReturnType,api.ReturnType);
        var request=new CheckPurchaseOrderImportRequest { Header=new(){BoNo="AT260820DQ"},
            Lines=[new(){ClientLineId=Guid.NewGuid(),IsNonStandard=true,Quantity=1,UnitPrice=0}] };
        var restored=JsonSerializer.Deserialize<CheckPurchaseOrderImportRequest>(JsonSerializer.Serialize(request))!;
        Assert.AreEqual(request.Lines[0].ClientLineId,restored.Lines[0].ClientLineId);
        Assert.IsTrue(restored.Lines[0].IsNonStandard);Assert.AreEqual("AT260820DQ",restored.Header.BoNo);
    }

    /// <summary>旧请求的指纹投影不增加新默认字段，新增快照与关联状态全部参与指纹。</summary>
    [TestMethod]
    public void LegacyFingerprintAndNewSnapshotCoverage()
    {
        var command=new CreateIndependentPurchaseOrderCommand {Lines=[new(){ProductId=Guid.NewGuid(),Quantity=1,UnitPrice=1}]};
        var legacy=JsonSerializer.SerializeToElement(IndependentPurchaseOrderInputPolicy.FingerprintInput(command));
        CollectionAssert.AreEquivalent(new[]{"CustomerId","SupplierId","OrderDate","Remark","PackagingRequirements","Lines"},
            legacy.EnumerateObject().Select(x=>x.Name).ToArray());
        CollectionAssert.AreEquivalent(new[]{"ProductId","Quantity","DeliverySupplierId","UnitPrice","TaxPrice","PlannedDeliveryDate","SupplierSku","DescriptionZh","SubNote"},
            legacy.GetProperty("Lines")[0].EnumerateObject().Select(x=>x.Name).ToArray());
        command.BoNo="Excel";
        var original=ProcurementOrderPolicy.Fingerprint(IndependentPurchaseOrderInputPolicy.FingerprintInput(command));
        command.BoNo="Excel2";
        Assert.AreNotEqual(original,ProcurementOrderPolicy.Fingerprint(IndependentPurchaseOrderInputPolicy.FingerprintInput(command)));
        command.BoNo="Excel";command.Lines[0].FeaturesZh="配置";
        Assert.AreNotEqual(original,ProcurementOrderPolicy.Fingerprint(IndependentPurchaseOrderInputPolicy.FingerprintInput(command)));
        command.Lines[0].FeaturesZh=null;command.Lines[0].AcceptLinkSkuMismatch=true;
        Assert.AreNotEqual(original,ProcurementOrderPolicy.Fingerprint(IndependentPurchaseOrderInputPolicy.FingerprintInput(command)));
    }

    /// <summary>批量查询保留歧义且参数化，不选择 TOP(1)；创建仓储明确持久化商业快照。</summary>
    [TestMethod]
    public void SqlScopeAndSnapshotContract()
    {
        var root=Root();
        var query=File.ReadAllText(Path.Combine(root,"SwiftExport.Infrastructure/Repositories/ForExcel/ForExcelPurchaseOrderRepository.cs"));
        StringAssert.Contains(query,"cp.CustomerId=@CustomerId");StringAssert.Contains(query,"IN @Skus");
        StringAssert.Contains(query,"LEFT JOIN dbo.Product");StringAssert.Contains(query,"WITH(HOLDLOCK)");
        StringAssert.Contains(query,"context.Session.Transaction");StringAssert.Contains(query,"cancellationToken: token");
        Assert.IsFalse(query.Contains("TOP(1)"));Assert.IsFalse(query.Contains("NOLOCK"));Assert.IsFalse(query.Contains("SELECT *"));
        Assert.IsFalse(query.Contains("INSERT "));Assert.IsFalse(query.Contains("UPDATE "));Assert.IsFalse(query.Contains("DELETE "));
        var write=File.ReadAllText(Path.Combine(root,"SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/ProcurementOrderDocumentRepository.cs"));
        foreach(var name in new[]{"BoNo","CustomerPoNo","ReplyDate","SoPerson","PoPerson","InletrHoseType","IMType","ZheKouBeiZhu","CustomerSku","LinkSku","FinishZh","FeaturesZh"})
            StringAssert.Contains(write,"@"+name);
    }

    /// <summary>只定位当前仓库根，不读取运行配置或连接字符串。</summary>
    private static string Root()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"SwiftExport-WebAPI.slnx")))return directory.FullName;
        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}

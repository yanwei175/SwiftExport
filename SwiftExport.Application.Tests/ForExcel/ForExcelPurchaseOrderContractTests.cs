using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.WebApi.Controllers.ForExcel;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
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

    /// <summary>PG 批量查询保留全部歧义候选且参数化，共享锁定已有关系和产品；原创建 SQL 必须持久化商业快照。静态检查不证明缺失范围保护。</summary>
    [TestMethod]
    public void SqlScopeAndSnapshotContract()
    {
        var root=Root();
        var query=File.ReadAllText(Path.Combine(root,"SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelPurchaseOrderRepository.cs"));
        StringAssert.Contains(query,"cp.customer_id=@CustomerId");StringAssert.Contains(query,"parameters.AddTexts(\"Skus\", skus)");
        StringAssert.Contains(query,"=ANY(");StringAssert.Contains(query,"FROM unnest(@Skus)");
        StringAssert.Contains(query,"LEFT JOIN LATERAL (");StringAssert.Contains(query,"FROM public.product source");
        StringAssert.Contains(query,"source.is_deleted=FALSE FOR SHARE");StringAssert.Contains(query,"FOR SHARE OF cp");
        StringAssert.Contains(query,"context.Session.Transaction");StringAssert.Contains(query,"cancellationToken: token");
        Assert.IsFalse(query.Contains("TOP(1)"));Assert.IsFalse(query.Contains("LIMIT 1",StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(query.Contains("NOLOCK"));Assert.IsFalse(query.Contains("SELECT *"));
        Assert.IsFalse(query.Contains("INSERT "));Assert.IsFalse(query.Contains("UPDATE "));Assert.IsFalse(query.Contains("DELETE "));
        AssertInsertSnapshot("Head",["DemandSourceType","DemandSourceUid","DemandSourceNo"],
            ["BoNo","CustomerPoNo","ReplyDate","SoPerson","PoPerson","InletrHoseType","IMType","ZheKouBeiZhu"]);
        AssertInsertSnapshot("Line",["SourceType","SourceNo"],["CustomerSku","LinkSku","FinishZh","FeaturesZh"]);
    }

    /// <summary>核对仓储实际插入 SQL 的列值配对；商业快照必须绑定对应参数，独立采购来源必须强制写空。</summary>
    private static void AssertInsertSnapshot(string part,string[] nullFields,string[] requiredFields)
    {
        var type=typeof(PostgreSqlProcurementOrderDocumentRepository);
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
        var map=(EntityMetadata)type.GetField(part,flags)!.GetValue(null)!;
        var sql=(string)type.GetField(part+"InsertSql",flags)!.GetRawConstantValue()!;
        StringAssert.Contains(sql,$"INSERT INTO \"{map.SchemaName}\".\"{map.TableName}\"");
        var insert=Regex.Match(sql,@"\((?<columns>.*?)\)\s*VALUES\s*\((?<values>.*?)\)\s*;",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        Assert.IsTrue(insert.Success,"插入语句必须提供明确的列和对应值。");
        var columns=insert.Groups["columns"].Value.Split(',').Select(value=>value.Trim().Trim('"')).ToArray();
        var values=insert.Groups["values"].Value.Split(',').Select(value=>value.Trim()).ToArray();
        Assert.AreEqual(columns.Length,values.Length,"每个插入列必须恰好对应一个值。");
        var bindings=columns.Zip(values).ToDictionary(pair=>pair.First,pair=>pair.Second,StringComparer.Ordinal);
        foreach(var name in requiredFields)
        {
            var column=map.GetProperty(name);
            Assert.IsTrue(bindings.TryGetValue(column.ColumnName,out var value),name+"必须写入快照列。");
            Assert.AreEqual("@"+column.ParameterName,value,name+"必须绑定对应快照参数。");
        }
        foreach(var name in nullFields)
            Assert.AreEqual("NULL",bindings[map.GetProperty(name).ColumnName].ToUpperInvariant(),name+"不能从输入伪造独立采购来源。");
    }

    /// <summary>只定位当前仓库根，不读取运行配置或连接字符串。</summary>
    private static string Root()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"SwiftExport-WebAPI.slnx")))return directory.FullName;
        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}

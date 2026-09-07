using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Shipping;

[TestClass]
public sealed class PackingListWorkspaceContractTests
{
    [TestMethod]
    public void ApiRoutes_AreFrozenAndMatchShippingBoundary()
    {
        var methods = typeof(IPackingListWorkspaceApi).GetMethods().ToDictionary(x => x.Name);
        Assert.AreEqual("/search", Post(methods["SearchAsync"]));
        Assert.AreEqual("/{uid}", Get(methods["GetDetailAsync"]));
        Assert.AreEqual("/sources/search", Post(methods["SearchSourcesAsync"]));
        Assert.AreEqual("/drafts", Post(methods["CreateDraftAsync"]));
        Assert.AreEqual("/drafts/{uid}", methods["UpdateDraftAsync"].GetCustomAttributes(typeof(PutAttribute), false).Cast<PutAttribute>().Single().Path);
        Assert.AreEqual("/{uid}/void", Post(methods["VoidAsync"]));
        Assert.AreEqual("/{uid}/post-preview", Post(methods["PostPreviewAsync"]));
        Assert.AreEqual("/{uid}/post", Post(methods["PostAsync"]));
        Assert.AreEqual("/{uid}/reverse-preview", Post(methods["ReversePreviewAsync"]));
        Assert.AreEqual("/{uid}/reverse", Post(methods["ReverseAsync"]));
        Assert.AreEqual("/{uid}/trace", Get(methods["GetTraceAsync"]));
        Assert.AreEqual("/{uid}/export", Get(methods["GetExportAsync"]));
        Assert.IsTrue(methods.Values.All(x => x.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))));
    }

    [TestMethod]
    public void NegativeStockExceptionType_RemainsReservedButPackingListDoesNotExposeAuthorizationEntry()
    {
        StringAssert.Contains(Read("SwiftExport.Contracts", "SysEnums", "Inventory", "InventoryTransactionTypeEnum.cs"), "NegativeStockExceptionOutbound = 90");
        var controller = Read("SwiftExport.WebApi", "Controllers", "Shipping", "PackingListWorkspaceController.cs");
        Assert.IsFalse(controller.Contains("negative", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(controller.Contains("override", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Posting_IsInternalAndNeverSelfCallsHttp()
    {
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingListWorkspaceService.cs");
        StringAssert.Contains(service, "IInboundPostingPort");
        StringAssert.Contains(service, "IOutboundPostingPort");
        Assert.IsFalse(service.Contains("HttpClient", StringComparison.Ordinal));
        StringAssert.Contains(service, "_session.Transaction.Commit()");
        StringAssert.Contains(service, "PackingList.Posted");
        StringAssert.Contains(service, "PackingList.Reversed");
    }

    [TestMethod]
    public void TargetPersistence_RemovesLegacyPackingListCompatibilityColumns()
    {
        var entity = Read("SwiftExport.Core", "Entities", "Shipping", "PackingListDocument.cs");
        var line = Read("SwiftExport.Core", "Entities", "Shipping", "PackingListContributionLine.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListDocumentRepository.cs");
        Assert.IsFalse(entity.Contains("LegacyHeadId", StringComparison.Ordinal));
        Assert.IsFalse(entity.Contains("IsLegacyReadOnly", StringComparison.Ordinal));
        Assert.IsFalse(line.Contains("LegacyPackingListHeadId", StringComparison.Ordinal));
        StringAssert.Contains(repository, "dbo.PackingListDocument");
        StringAssert.Contains(repository, "dbo.PackingListContributionLine");
    }

    [TestMethod]
    public void SalesAndProcurementShippedFacts_UsePostedFinalOutbound()
    {
        var sales = Read("SwiftExport.Infrastructure", "Repositories", "Orders", "SalesOrderHeadRepository.cs");
        StringAssert.Contains(sales, "outHead.DocumentType=20 AND outHead.PostingStatus=1");
        Assert.IsFalse(sales.Contains("SUM(d.ShippedQty)", StringComparison.Ordinal));
        var procurement = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        StringAssert.Contains(procurement, "outHead.DocumentType=20 AND outHead.PostingStatus=1");
        StringAssert.Contains(procurement, "FinalShippedQty");
    }

    [TestMethod]
    public void Trace_ResolvesAllActiveExecutionLinksFromAllocation()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListWorkspaceRepository.cs");
        StringAssert.Contains(repository, "link.NetDemandAllocationId=pl.NetDemandAllocationId");
        StringAssert.Contains(repository, "link.LinkStatus=0");
        StringAssert.Contains(repository, "DemandExecutionLink");
    }

    [TestMethod]
    public void PlanningSourcesAreAnchoredByGrossDemandAndAllocationIsOptionalTrace()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListWorkspaceRepository.cs");
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingListWorkspaceService.cs");
        var entity = Read("SwiftExport.Core", "Entities", "Shipping", "PackingListContributionLine.cs");
        StringAssert.Contains(repository, "FROM dbo.GrossDemand g");
        StringAssert.Contains(repository, "g.SourceType IN('SO','CR')");
        StringAssert.Contains(repository, "l.GrossDemandId=g.Uid");
        StringAssert.Contains(service, "sourceDelivery.VerifyForUpdateAsync");
        StringAssert.Contains(service, "GrossDemandId = x.GrossDemandId");
        StringAssert.Contains(entity, "public Guid? NetDemandId");
        StringAssert.Contains(entity, "public Guid? NetDemandAllocationId");
    }

    private static string Post(System.Reflection.MethodInfo method) => method.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path;
    private static string Get(System.Reflection.MethodInfo method) => method.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single().Path;
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}

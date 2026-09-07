using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using System.IO;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>验证销售确认接入来源需求以及履约 Manager/Detail 查询的分层契约。</summary>
[TestClass]
public sealed class SalesSourceDemandAndFulfillmentContractTests
{
    [TestMethod]
    public void ConfirmCommandCarriesOnlyIdentityVersionAndIdempotencyKey()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Uid", "Version", "CommandId" },
            typeof(ConfirmSalesOrderCommand).GetProperties().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void SalesConfirmationUsesPostConfirmVersionProductUnitAndReceptionBoundary()
    {
        var service = Read("SwiftExport.Application", "Services", "Orders", "SalesOrderDetailService.cs");
        var statusWrite = service.IndexOf("SetConfirmationStatusAsync", StringComparison.Ordinal);
        var reception = service.IndexOf("sourceDemandReceptionService.ReceiveAsync", StringComparison.Ordinal);

        Assert.IsTrue(statusWrite >= 0 && reception > statusWrite);
        StringAssert.Contains(service, "SourceVersion = sourceVersion");
        StringAssert.Contains(service, "UnitSnapshot = product?.Unit");
        StringAssert.Contains(service, "RejectUnsubmittedActiveLines = true");
        Assert.IsFalse(service.Contains("IGrossDemandRepository", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("new GrossDemand", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FulfillmentQueriesKeepSalesLinesAndAggregateFactsBeforeJoining()
    {
        var detail = Read("SwiftExport.Infrastructure", "Repositories", "Orders", "SalesFulfillmentDetailRepository.cs");
        var manager = Read("SwiftExport.Infrastructure", "Repositories", "Orders", "SalesFulfillmentManagerRepository.cs");

        StringAssert.Contains(detail, "FROM dbo.SalesOrderLine l");
        StringAssert.Contains(detail, "WITH LinkFacts AS");
        StringAssert.Contains(detail, "SOURCE_DEMAND_MISSING");
        StringAssert.Contains(detail, "NOT EXISTS");
        StringAssert.Contains(detail, "dbo.SupplierProgressReport");
        StringAssert.Contains(detail, "dbo.OutboundDocumentLine");
        StringAssert.Contains(manager, "dbo.OutboundDocumentLine");
        StringAssert.Contains(manager, "NetDemandAllocation");
    }

    [TestMethod]
    public void SchemaAndPreviewUseFullSourceIdentityWithoutGuessingHistoricalVersion()
    {
        var fresh = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "030_初始化目标业务表.sql");
        var preview = Read("SwiftExport.Infrastructure", "Migrations", "024_SourceDemandReception_Preflight.sql");
        var migration = Read("SwiftExport.Infrastructure", "Migrations", "024_SourceDemandReception_AdditiveSchema.sql");

        StringAssert.Contains(fresh, "SourceVersion int NULL,CustomerId uniqueidentifier NULL");
        StringAssert.Contains(fresh, "SourceType,SourceUid,SourceLineId");
        StringAssert.Contains(migration, "SourceType,SourceUid,SourceLineId");
        StringAssert.Contains(preview, "无法确定历史接收版本，不自动回填");
        Assert.IsFalse(preview.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(preview.Contains("INSERT ", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RefitAndControllersExposeMatchingManagerAndDetailRoutes()
    {
        var managerApi = Read("SwiftExport.Contracts", "Apis", "SalesOrderApis", "ISalesFulfillmentManagerApi.cs");
        var detailApi = Read("SwiftExport.Contracts", "Apis", "SalesOrderApis", "ISalesFulfillmentDetailApi.cs");
        var managerController = Read("SwiftExport.WebApi", "Controllers", "Orders", "SalesFulfillmentManagerController.cs");
        var detailController = Read("SwiftExport.WebApi", "Controllers", "Orders", "SalesFulfillmentDetailController.cs");

        StringAssert.Contains(managerApi, "[Post(\"/search\")]");
        StringAssert.Contains(managerController, "[Route(\"api/sales-fulfillment/manager\")]");
        StringAssert.Contains(managerController, "[HttpPost(\"search\")]");
        StringAssert.Contains(detailApi, "[Get(\"/{salesOrderId}\")]");
        StringAssert.Contains(detailController, "[Route(\"api/sales-fulfillment/detail\")]");
        StringAssert.Contains(detailController, "[HttpGet(\"{salesOrderId:guid}\")]");
    }

    [TestMethod]
    public void ReceptionLocksIdempotencyAndSourceRangesInsideCallerTransaction()
    {
        var reception = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandReceptionService.cs");
        var demandRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var logRepository = Read("SwiftExport.Infrastructure", "Repositories", "Auditing", "BusinessOperationLogRepository.cs");

        StringAssert.Contains(reception, "TRANSACTION_REQUIRED");
        StringAssert.Contains(demandRepository, "WITH (UPDLOCK, HOLDLOCK)");
        StringAssert.Contains(logRepository, "WITH(UPDLOCK,HOLDLOCK)");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    /// <summary>从测试输出目录向上定位仓库根目录。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}

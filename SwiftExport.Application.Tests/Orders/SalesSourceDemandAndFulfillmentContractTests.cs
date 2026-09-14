using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using System.IO;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>验证销售确认接入来源需求的分层契约。</summary>
[TestClass]
public sealed class SalesSourceDemandContractTests
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

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

    /// <summary>销售取消确认必须先完成来源退出，且不得自动替用户执行物料回退。</summary>
    [TestMethod]
    public void SalesCancellationUsesSourceExitBeforeChangingConfirmationStatus()
    {
        var service = Read("SwiftExport.Application", "Services", "Orders", "SalesOrderDetailService.cs");
        var start = service.IndexOf("public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync", StringComparison.Ordinal);
        var end = service.IndexOf("public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync", start, StringComparison.Ordinal);
        var method = service[start..end];
        var sourceExit = method.IndexOf("sourceDemandReceptionService.CancelConfirmationAsync", StringComparison.Ordinal);
        var statusWrite = method.IndexOf("SetConfirmationStatusAsync", StringComparison.Ordinal);

        Assert.IsTrue(sourceExit >= 0 && statusWrite > sourceExit);
        StringAssert.Contains(method, "SourceDemandReceptionException ex");
        StringAssert.Contains(method, "ex.Code");
        Assert.IsFalse(method.Contains("RollbackSourceEstablishmentAsync", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("RollbackAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GeneratedSchemaUsesFullSourceIdentityWithoutHistoricalMigration()
    {
        var baseline = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        StringAssert.Contains(baseline, "[SourceVersion] int NULL");
        StringAssert.Contains(baseline, "[CustomerId] uniqueidentifier NULL");
        StringAssert.Contains(baseline, "[SourceType] nvarchar");
        StringAssert.Contains(baseline, "[SourceUid] uniqueidentifier");
        StringAssert.Contains(baseline, "[SourceLineId] uniqueidentifier");
        Assert.IsFalse(baseline.Contains("UPDATE dbo.", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(baseline.Contains("INSERT dbo.", StringComparison.OrdinalIgnoreCase));
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

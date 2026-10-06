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
    public void SalesConfirmationUsesPostConfirmVersionSalesUnitSnapshotAndReceptionBoundary()
    {
        var service = Read("SwiftExport.Application", "Services", "SalesOrders", "Confirmation", "SalesOrderConfirmationService.cs");
        var statusWrite = service.IndexOf("SetConfirmationStatusAsync", StringComparison.Ordinal);
        var reception = service.IndexOf("reception.ReceiveAsync", StringComparison.Ordinal);

        Assert.IsTrue(statusWrite >= 0 && reception > statusWrite);
        StringAssert.Contains(service, "SourceVersion = sourceVersion");
        StringAssert.Contains(service, "UnitSnapshot = line.UnitSnapshot");
        StringAssert.Contains(service, "RejectUnsubmittedActiveLines = true");
        Assert.IsFalse(service.Contains("IGrossDemandRepository", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("new GrossDemand", StringComparison.Ordinal));
    }

    /// <summary>销售取消确认必须先完成来源退出，且不得自动替用户执行物料回退。</summary>
    [TestMethod]
    public void SalesCancellationUsesSourceExitBeforeChangingConfirmationStatus()
    {
        var service = Read("SwiftExport.Application", "Services", "SalesOrders", "Confirmation", "SalesOrderCancellationService.cs");
        var start = service.IndexOf("public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync", StringComparison.Ordinal);
        var end = service.IndexOf("private async Task WriteCancellationLogAsync", start, StringComparison.Ordinal);
        var method = service[start..end];
        var sourceExit = method.IndexOf("sourceDemandReceptionService.CancelConfirmationAsync", StringComparison.Ordinal);
        var statusWrite = method.IndexOf("SetConfirmationStatusAsync", StringComparison.Ordinal);

        Assert.IsTrue(sourceExit >= 0 && statusWrite > sourceExit);
        StringAssert.Contains(method, "SourceDemandReceptionException ex");
        StringAssert.Contains(method, "ex.Code");
        Assert.IsFalse(method.Contains("RollbackSourceEstablishmentAsync", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("RollbackAsync", StringComparison.Ordinal));
    }

    /// <summary>PG 基线保存完整来源身份及可空历史字段，只建结构，不回填单据历史。</summary>
    [TestMethod]
    public void GeneratedSchemaUsesFullSourceIdentityWithoutHistoricalMigration()
    {
        var baseline = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql");
        StringAssert.Contains(baseline, "\"source_version\" integer NULL");
        StringAssert.Contains(baseline, "\"customer_id\" uuid NULL");
        StringAssert.Contains(baseline, "\"source_type\" varchar");
        StringAssert.Contains(baseline, "\"source_uid\" uuid");
        StringAssert.Contains(baseline, "\"source_line_id\" uuid");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(baseline, @"\bUPDATE\s+(?:public|""public"")\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(baseline, @"\bINSERT\s+(?:INTO\s+)?(?:public|""public"")\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    /// <summary>来源接收继续借用调用方事务，并使用 PG 既有范围准备和行锁；审计命令读取沿用现有 PG 互斥和行锁。</summary>
    [TestMethod]
    public void ReceptionLocksIdempotencyAndSourceRangesInsideCallerTransaction()
    {
        var reception = Read("SwiftExport.Application", "Services", "DemandPlanning", "Sources", "Reception", "SourceDemandReceptionService.cs");
        var demandRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Sources", "Document", "PostgreSqlGrossDemandDocumentRepository.cs");
        var logRepository = Read("SwiftExport.Infrastructure", "Repositories", "Auditing", "PostgreSqlBusinessOperationLogRepository.cs");

        StringAssert.Contains(reception, "TRANSACTION_REQUIRED");
        StringAssert.Contains(demandRepository, "PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync");
        StringAssert.Contains(demandRepository, "InScope([sourceUid], []");
        StringAssert.Contains(demandRepository, "FOR UPDATE;");
        StringAssert.Contains(logRepository, "await LockCommandAsync(commandId);");
        StringAssert.Contains(logRepository, "command_id=@CommandId AND is_deleted=FALSE FOR UPDATE");
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

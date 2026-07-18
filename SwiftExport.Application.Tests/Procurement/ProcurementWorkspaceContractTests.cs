using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Procurement;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Application.Tests.Procurement;

[TestClass]
public sealed class ProcurementWorkspaceContractTests
{
    [TestMethod]
    public void ApiRoutes_MustMatchProcurementController()
    {
        var methods = typeof(IPurchaseOrderWorkspaceApi).GetMethods().ToDictionary(x => x.Name);
        Assert.AreEqual("/search", methods["SearchAsync"].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/{uid}/draft", methods["UpdateDraftAsync"].GetCustomAttributes(typeof(PutAttribute), false).Cast<PutAttribute>().Single().Path);
        Assert.AreEqual("/{uid}/confirm", methods["ConfirmAsync"].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/{uid}/cancel-confirmation", methods["CancelConfirmationAsync"].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/{uid}/progress", methods["ReportProgressAsync"].GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.IsTrue(methods.Values.All(x => x.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))));
    }

    [TestMethod]
    public void SupplierArrangementValues_AreFrozen()
    {
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 8 },
            Enum.GetValues<SupplierArrangementStatusEnum>().Select(x => (int)x).ToArray());
    }

    [TestMethod]
    public void RepositorySql_MustUseStablePagingAndLatestSequence()
    {
        var source = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        StringAssert.Contains(source, "ORDER BY h.OrderDate DESC,h.Uid DESC");
        StringAssert.Contains(source, "ORDER BY f.Sequence DESC,f.Uid DESC");
        StringAssert.Contains(source, "COALESCE(latest.ReadyQty,0)");
        Assert.IsFalse(source.Contains("l.ReadyQty AS LatestReadyQty", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Commands_MustUsePersistentIdempotencyAndNoMemoryDebounce()
    {
        var controller = Read("SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrderWorkspaceController.cs");
        Assert.IsFalse(controller.Contains("PreventDuplicateRequest", StringComparison.Ordinal));
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        StringAssert.Contains(service, "operationLogs.GetByCommandAsync");
        StringAssert.Contains(service, "Outcome = ProcurementCommandOutcomeEnum.AlreadyApplied");
        StringAssert.Contains(service, "_session.Transaction.Commit()");
    }

    [TestMethod]
    public void Confirmation_MustDistinguishNullAndExplicitZeroPrice()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        StringAssert.Contains(service, "!line.TaxPrice.HasValue || line.TaxPrice.Value < 0");
        StringAssert.Contains(service, "明确的零价允许确认");
        StringAssert.Contains(service, "linked != line.Quantity");
    }

    [TestMethod]
    public void Progress_MustBeAppendOnlyAndRequireCorrectionReason()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "SupplierProgressReportRepository.cs");
        StringAssert.Contains(repository, "INSERT dbo.FollowUpLog");
        Assert.IsFalse(repository.Contains("UPDATE dbo.FollowUpLog", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(repository.Contains("DELETE FROM dbo.FollowUpLog", StringComparison.OrdinalIgnoreCase));
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        StringAssert.Contains(service, "command.ReadyQty < latest.ReadyQty");
        StringAssert.Contains(service, "累计已备好数量下降时必须填写纠错原因");
    }

    [TestMethod]
    public void BusinessLog_MustShareTransactionAndWhitelistChanges()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Auditing", "BusinessOperationLogRepository.cs");
        StringAssert.Contains(repository, "Session.Transaction");
        StringAssert.Contains(repository, "INSERT dbo.BusinessOperationLog");
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        Assert.IsFalse(service.Contains("Password", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(service, "PlannedDeliveryDate");
        StringAssert.Contains(service, "SupplierSku");
    }

    [TestMethod]
    public void Migration_IsAdditiveAndDoesNotRecreatePurchaseTables()
    {
        var sql = Read("SwiftExport.Infrastructure", "Migrations", "009_ProcurementWorkspace_AdditiveSchema.sql");
        StringAssert.Contains(sql, "ALTER TABLE dbo.PurchaseOrderHead ADD ConfirmedDate");
        StringAssert.Contains(sql, "CREATE TABLE dbo.FollowUpLog");
        StringAssert.Contains(sql, "CREATE TABLE dbo.BusinessOperationLog");
        StringAssert.Contains(sql, "THROW 51000");
        Assert.IsFalse(sql.Contains("CREATE TABLE dbo.PurchaseOrderHead", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Workspace_MustReadOfficialSourceAndInboundFacts()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        StringAssert.Contains(repository, "h.DemandSourceNo");
        StringAssert.Contains(repository, "COALESCE(p.MainImage,N'') AS MainImage");
        StringAssert.Contains(repository, "receiveLine.SourceType=10");
        StringAssert.Contains(repository, "receiveLink.TargetLineId=l.Uid");
        StringAssert.Contains(repository, "receiveHead.PostingStatus=1");
        Assert.IsFalse(repository.Contains("h.SalesOrderNo", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("l.RecQty", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DraftUpdate_MustWhitelistLegacyCommercialFields()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        foreach (var field in new[] { "BoNo", "CustomerPoNo", "SoPerson", "PoPerson", "ReplyDate", "InletrHoseType", "AeratorType", "PalletType", "BoxType", "LabelType", "IMType", "CartonType", "LogoType", "ZheKouBeiZhu" })
            StringAssert.Contains(service, field);
        Assert.IsFalse(service.Contains("head.SupplierId = command", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("line.Quantity =", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OfficialEntities_MustMapCleanPurchaseTables()
    {
        Assert.AreEqual("PurchaseOrderHead", typeof(PurchaseOrderHead).GetCustomAttributes(typeof(TableAttribute), false).Cast<TableAttribute>().Single().Name);
        Assert.AreEqual("PurchaseOrderLine", typeof(PurchaseOrderLine).GetCustomAttributes(typeof(TableAttribute), false).Cast<TableAttribute>().Single().Name);
        Assert.AreEqual("QTY", typeof(PurchaseOrderLine).GetProperty(nameof(PurchaseOrderLine.Quantity))!
            .GetCustomAttributes(typeof(ColumnAttribute), false).Cast<ColumnAttribute>().Single().Name);

        var headProperties = typeof(PurchaseOrderHead).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var lineProperties = typeof(PurchaseOrderLine).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in new[] { "OrderStatus", "SalesOrderNo", "LineCount", "TotalQty", "TotalReadyQty", "TotalShippedQty" })
            Assert.IsFalse(headProperties.Contains(removed), $"采购头不应继续缓存 {removed}。");
        foreach (var removed in new[] { "ReadyStatus", "ReadyQty", "RecQty", "LastRecDate", "DeliveryTo" })
            Assert.IsFalse(lineProperties.Contains(removed), $"采购行不应继续缓存 {removed}。");
    }

    [TestMethod]
    public void DevelopmentRebuild_MustBeExplicitlyGuardedAndMatchOfficialModel()
    {
        var sql = Read("SwiftExport.Infrastructure", "Migrations", "015_PurchaseOrder_Rebuild_DevelopmentOnly.sql");
        StringAssert.Contains(sql, "DECLARE @AllowDestructiveRebuild bit = 0");
        StringAssert.Contains(sql, "DROP TABLE dbo.PurchaseOrderLine");
        StringAssert.Contains(sql, "CREATE TABLE dbo.PurchaseOrderHead");
        StringAssert.Contains(sql, "CREATE TABLE dbo.PurchaseOrderLine");
        StringAssert.Contains(sql, "QTY decimal(18,4) NOT NULL");
        StringAssert.Contains(sql, "UnitSnapshot nvarchar(50) NULL");
        Assert.IsFalse(sql.Contains("ReadyQty", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("RecQty", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}

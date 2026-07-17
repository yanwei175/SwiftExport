using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.SysEnums;

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
        StringAssert.Contains(service, "linked != line.QTY");
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
    public void LegacyFieldCoverage_MustRemainAdditiveAndExplicit()
    {
        var sql = Read("SwiftExport.Infrastructure", "Migrations", "010_ProcurementWorkspace_LegacyFieldCoverage.sql");
        StringAssert.Contains(sql, "COL_LENGTH(N'dbo.PurchaseOrderHead',N'BoNo')");
        StringAssert.Contains(sql, "COL_LENGTH(N'dbo.PurchaseOrderLine',N'FinishZh')");
        Assert.IsFalse(sql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("UPDATE dbo.PurchaseOrderHead", StringComparison.OrdinalIgnoreCase));
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        StringAssert.Contains(repository, "COALESCE(h.DemandSourceNo,h.SalesOrderNo) AS DemandSourceNo");
        StringAssert.Contains(repository, "COALESCE(p.MainImage,N'') AS MainImage");
        StringAssert.Contains(repository, "N'尚未接入' AS UnreceivedQtyText");
    }

    [TestMethod]
    public void DraftUpdate_MustWhitelistLegacyCommercialFields()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrderWorkspaceService.cs");
        foreach (var field in new[] { "BoNo", "CustomerPoNo", "SoPerson", "PoPerson", "ReplyDate", "InletrHoseType", "AeratorType", "PalletType", "BoxType", "LabelType", "IMType", "CartonType", "LogoType", "ZheKouBeiZhu" })
            StringAssert.Contains(service, field);
        Assert.IsFalse(service.Contains("head.SupplierId =", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("line.QTY =", StringComparison.Ordinal));
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

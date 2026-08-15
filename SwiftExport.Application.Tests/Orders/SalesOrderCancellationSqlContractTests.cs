using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Orders;

[TestClass]
public sealed class SalesOrderCancellationSqlContractTests
{
    [TestMethod]
    public void RepositorySql_MustKeepHardDeleteAndHistoryPreservationStrict()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "GrossDemandRepository.cs");

        StringAssert.Contains(source, "HardDeleteNeverDecomposedAsync");
        StringAssert.Contains(source, "d.[DecompositionRevision]=0");
        StringAssert.Contains(source, "NOT EXISTS(SELECT 1 FROM [dbo].[NetDemand]");
        StringAssert.Contains(source, "VoidRolledBackForSourceCancellationAsync");
        StringAssert.Contains(source, "nd.[LifecycleStatus] NOT IN (@NetDemandRolledBack,@NetDemandVoided)");
        StringAssert.Contains(source, "allocation.[LifecycleStatus] NOT IN (@AllocationSuperseded,@AllocationCancelled)");
        StringAssert.Contains(source, "WITH (UPDLOCK,HOLDLOCK)");
    }

    [TestMethod]
    public void CleanupMigration_MustDefaultToReadOnlyAndRejectBlockedBatch()
    {
        var script = Read(
            "SwiftExport.Infrastructure",
            "Migrations",
            "018_SalesOrderGrossDemandCancellationCleanup.sql");

        StringAssert.Contains(script, "DECLARE @Execute bit = 0");
        StringAssert.Contains(script, "IF @Execute = 0");
        StringAssert.Contains(script, "ROLLBACK TRANSACTION");
        StringAssert.Contains(script, "[Disposition]='BLOCKED'");
        StringAssert.Contains(script, "THROW 51001");
        StringAssert.Contains(script, "'HARD_DELETE'");
        StringAssert.Contains(script, "'RESTORE_VOID'");
        StringAssert.Contains(script, "COMMIT TRANSACTION");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}

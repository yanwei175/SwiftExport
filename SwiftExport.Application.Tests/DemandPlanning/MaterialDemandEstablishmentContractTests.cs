using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证来源需求到物料需求的职责迁移和持久化契约。</summary>
[TestClass]
public sealed class MaterialDemandEstablishmentContractTests
{
    [TestMethod]
    public void MaterialUseCase_MustOwnCandidateEstablishRollbackAndBasisSnapshots()
    {
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "MaterialDemandEstablishmentService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "NetDemandRepository.cs");

        StringAssert.Contains(service, "GetByIdForUpdateAsync");
        StringAssert.Contains(service, "GetNextDecompositionRevisionForUpdateAsync");
        StringAssert.Contains(service, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(service, "GetByCommandAsync(Module, EstablishOperation");
        StringAssert.Contains(service, "GetRollbackBlockersAsync");
        Assert.IsFalse(service.Contains("UpdateDecompositionStateAsync", StringComparison.Ordinal));

        StringAssert.Contains(repository, "GetSourceDemandCandidateGroupsAsync");
        StringAssert.Contains(repository, "GetSourceDemandCandidatesBySourceAsync");
        StringAssert.Contains(repository, "GROUP BY d.[SourceType],d.[SourceUid]");
        StringAssert.Contains(repository, "d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid");
        StringAssert.Contains(repository, "d.[LinkSkuSnapshot] AS [LinkSku]");
        StringAssert.Contains(repository, "d.[ImagePathSnapshot] AS [ImagePath]");
        Assert.IsFalse(repository.Contains("OUTER APPLY", StringComparison.Ordinal));
        StringAssert.Contains(repository, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(repository, "WITH (UPDLOCK,HOLDLOCK)");
    }

    [TestMethod]
    public void SourceBoundary_MustNotExposeMaterialCommandsOrReadMaterialRepository()
    {
        var api = Read("SwiftExport.Contracts", "Apis", "DemandPlanning", "IGrossDemandWorkspaceApi.cs");
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "Sources", "Manual", "ManualGrossDemandService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");

        Assert.IsFalse(api.Contains("DecomposeAsync", StringComparison.Ordinal));
        Assert.IsFalse(api.Contains("RollbackAsync", StringComparison.Ordinal));
        Assert.IsFalse(api.Contains("GetTraceAsync", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("INetDemandRepository", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("INetDemandAllocationRepository", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("HasSourceCancellationBlockerAsync", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("GetNetDemandHistoryCountsForUpdateAsync", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}

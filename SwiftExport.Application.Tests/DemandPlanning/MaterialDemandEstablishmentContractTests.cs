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
        StringAssert.Contains(service, "GetActiveRollbackBlockersAsync");
        Assert.IsFalse(service.Contains("UpdateDecompositionStateAsync", StringComparison.Ordinal));

        StringAssert.Contains(repository, "SearchSourceDemandCandidatesAsync");
        StringAssert.Contains(repository, "OUTER APPLY");
        StringAssert.Contains(repository, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(repository, "WITH (UPDLOCK,HOLDLOCK)");
    }

    [TestMethod]
    public void SourceBoundary_MustNotExposeMaterialCommandsOrReadMaterialRepository()
    {
        var api = Read("SwiftExport.Contracts", "Apis", "DemandPlanning", "IGrossDemandWorkspaceApi.cs");
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "GrossDemandWorkspaceService.cs");
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

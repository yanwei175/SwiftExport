using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning.Materials;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Core.Entities.Auditing;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证来源需求到物料需求的职责迁移和持久化契约。</summary>
[TestClass]
public sealed class MaterialDemandEstablishmentContractTests
{
    /// <summary>保留建立排序与回退原因规范化的既有审计重放指纹语义。</summary>
    [TestMethod]
    public void CommandFingerprintAndReplayKeepExistingNormalization()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var establish = new EstablishMaterialDemandsCommand
        {
            Items =
            [
                new() { GrossDemandId = secondId, SourceDemandVersion = 2, SourceVersion = 3 },
                new() { GrossDemandId = firstId, SourceDemandVersion = 1, SourceVersion = 2 }
            ]
        };
        var reordered = new EstablishMaterialDemandsCommand { Items = [.. establish.Items.AsEnumerable().Reverse()] };
        Assert.AreEqual(MaterialDemandCommandSupport.CreateFingerprint(establish),
            MaterialDemandCommandSupport.CreateFingerprint(reordered));

        var rollback = new RollbackMaterialDemandsCommand
        {
            Reason = "  来源重做  ", Items = [new() { GrossDemandId = firstId, DecompositionRevision = 1 }]
        };
        var trimmed = new RollbackMaterialDemandsCommand
        {
            Reason = "来源重做", Items = [new() { GrossDemandId = firstId, DecompositionRevision = 1 }]
        };
        Assert.AreEqual(MaterialDemandCommandSupport.CreateFingerprint(rollback),
            MaterialDemandCommandSupport.CreateFingerprint(trimmed));

        var fingerprint = MaterialDemandCommandSupport.CreateFingerprint(rollback);
        var log = new BusinessOperationLog { RelatedObjectsJson = fingerprint, ChangedFieldsJson = "3" };
        Assert.AreEqual(3, MaterialDemandCommandSupport.Replay<int>(log, fingerprint));
        Assert.Throws<MaterialDemandException>(() => MaterialDemandCommandSupport.Replay<int>(log, "different"));
    }

    [TestMethod]
    public void MaterialUseCase_MustOwnCandidateEstablishRollbackAndBasisSnapshots()
    {
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Establishment", "MaterialDemandEstablishmentService.cs");
        var reversal = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Reversal", "MaterialDemandReversalService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "NetDemandRepository.cs");

        StringAssert.Contains(service, "GetByIdForUpdateAsync");
        StringAssert.Contains(service, "GetNextDecompositionRevisionForUpdateAsync");
        StringAssert.Contains(service, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(service, "GetByCommandAsync(Module, EstablishOperation");
        StringAssert.Contains(reversal, "GetRollbackBlockersAsync");
        StringAssert.Contains(reversal, "GetByCommandAsync(Module, RollbackOperation");
        Assert.IsFalse(service.Contains("RollbackRevisionAsync", StringComparison.Ordinal));
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

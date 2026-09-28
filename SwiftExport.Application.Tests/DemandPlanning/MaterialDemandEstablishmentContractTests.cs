using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证来源需求到物料需求的职责迁移和持久化契约。</summary>
[TestClass]
public sealed class MaterialDemandEstablishmentContractTests
{
    /// <summary>核对两项命令按稳定输入使用公共回执，审计不再承担机器重放。</summary>
    [TestMethod]
    public async Task MaterialCommandsUsePublicReceiptAndIndependentPermissions()
    {
        var establishment = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Establishment", "MaterialDemandEstablishmentService.cs");
        var reversal = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Reversal", "MaterialDemandReversalService.cs");
        foreach (var source in new[] { establishment, reversal })
        {
            StringAssert.Contains(source, "pipeline.ExecuteAsync");
            StringAssert.Contains(source, "executor.ExecuteAsync");
            StringAssert.Contains(source, "OrderBy(item => item.GrossDemandId)");
            Assert.IsFalse(source.Contains("GetByCommandAsync", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("BeginTransaction", StringComparison.Ordinal));
        }
        StringAssert.Contains(establishment, "PermissionKeys.DemandPlanningMaterialEstablish");
        StringAssert.Contains(reversal, "PermissionKeys.DemandPlanningMaterialRollback");
        var userId = Guid.NewGuid();
        var evaluator = new PermissionEvaluationService(new MaterialPermissionFacts(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, RoleCodes = ["MATERIAL_ESTABLISH"],
            PermissionKeys = [PermissionKeys.DemandPlanningMaterialEstablish]
        }));
        Assert.IsTrue(await evaluator.HasPermissionAsync(userId, PermissionKeys.DemandPlanningMaterialEstablish));
        Assert.IsFalse(await evaluator.HasPermissionAsync(userId, PermissionKeys.DemandPlanningMaterialRollback));
    }

    [TestMethod]
    public void MaterialUseCase_MustOwnCandidateEstablishRollbackAndBasisSnapshots()
    {
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Establishment", "MaterialDemandEstablishmentService.cs");
        var reversal = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "Reversal", "MaterialDemandReversalService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Materials", "Sources", "MaterialDemandSourceRepository.cs");
        var documentRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Materials", "Document", "MaterialDemandDocumentRepository.cs");

        StringAssert.Contains(service, "GetByIdForUpdateAsync");
        StringAssert.Contains(service, "GetNextDecompositionRevisionForUpdateAsync");
        StringAssert.Contains(service, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(service, "EstablishInTransactionAsync");
        StringAssert.Contains(reversal, "GetRollbackBlockersAsync");
        StringAssert.Contains(reversal, "ReverseInTransactionAsync");
        Assert.IsFalse(service.Contains("RollbackRevisionAsync", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("UpdateDecompositionStateAsync", StringComparison.Ordinal));

        StringAssert.Contains(repository, "GetSourceDemandCandidateGroupsAsync");
        StringAssert.Contains(repository, "GetSourceDemandCandidatesBySourceAsync");
        StringAssert.Contains(repository, "GROUP BY d.[SourceType],d.[SourceUid]");
        StringAssert.Contains(repository, "d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid");
        StringAssert.Contains(repository, "d.[LinkSkuSnapshot] AS [LinkSku]");
        StringAssert.Contains(repository, "d.[ImagePathSnapshot] AS [ImagePath]");
        var establishmentCandidates = repository.Split("GetPurchaseCandidateSourceGroupsAsync", StringSplitOptions.None)[0];
        Assert.IsFalse(establishmentCandidates.Contains("OUTER APPLY", StringComparison.Ordinal));
        StringAssert.Contains(documentRepository, "SourceDemandBasisFingerprintSnapshot");
        StringAssert.Contains(documentRepository, "WITH (UPDLOCK,HOLDLOCK)");
    }

    [TestMethod]
    public void SourceBoundary_MustNotExposeMaterialCommandsOrReadMaterialRepository()
    {
        var api = Read("SwiftExport.Contracts", "Apis", "DemandPlanning", "Sources", "IGrossDemandWorkspaceApi.cs");
        var service = Read("SwiftExport.Application", "Services", "DemandPlanning", "Sources", "Manual", "ManualGrossDemandService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Sources", "Document", "GrossDemandDocumentRepository.cs");

        Assert.IsFalse(api.Contains("DecomposeAsync", StringComparison.Ordinal));
        Assert.IsFalse(api.Contains("RollbackAsync", StringComparison.Ordinal));
        Assert.IsFalse(api.Contains("GetTraceAsync", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("INetDemandRepository", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("INetDemandAllocationRepository", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("HasSourceCancellationBlockerAsync", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("GetNetDemandHistoryCountsForUpdateAsync", StringComparison.Ordinal));
    }

    /// <summary>只提供单个角色的权限事实，验证独立操作键的评估结果。</summary>
    private sealed class MaterialPermissionFacts(UserPermissionFacts facts) : IUserPermissionRepository
    {
        public Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserPermissionFacts?>(userId == facts.UserId ? facts : null);

        public Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserSummaryDto>>([]);
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

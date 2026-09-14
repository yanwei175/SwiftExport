using Dapper;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>为物料事实修改及采购承接提供来源优先的物料树锁定。</summary>
public sealed partial class NetDemandRepository
{
    /// <summary>先锁定有效来源，再按稳定行序读取并锁定同一物料树，供调用方事务复核。</summary>
    public async Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId)
    {
        const string locatorSql = @"
SELECT [GrossDemandId],[RootNetDemandId]
FROM [dbo].[NetDemand] WITH (NOLOCK)
WHERE [Uid]=@NetDemandId AND [LifecycleStatus]=@NetDemandActive AND [IsDeleted]=0;";
        var locator = await Connection.QuerySingleOrDefaultAsync<FamilyLocator>(
            locatorSql,
            new { NetDemandId = netDemandId, NetDemandActive = SwiftExport.Contracts.SysEnums.NetDemandLifecycleStatusEnum.Active },
            Transaction);
        if (locator is null) return [];

        const string grossLockSql = @"
SELECT [Uid]
FROM [dbo].[GrossDemand] WITH (UPDLOCK,HOLDLOCK)
WHERE [Uid]=@GrossDemandId AND [LifecycleStatus]=@GrossDemandActive AND [IsDeleted]=0;";
        var lockedGrossDemandId = await Connection.QuerySingleOrDefaultAsync<Guid?>(
            grossLockSql,
            new
            {
                locator.GrossDemandId,
                GrossDemandActive = SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Active
            }, Transaction);
        if (!lockedGrossDemandId.HasValue) return [];
        var rows = await Connection.QueryAsync<NetDemand>(NetDemandSelect + @"
FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
WHERE nd.[GrossDemandId]=@GrossDemandId AND nd.[RootNetDemandId]=@RootNetDemandId
  AND nd.[LifecycleStatus]=@Active AND nd.[IsDeleted]=0
ORDER BY nd.[LineNumber],nd.[Uid];",
            new
            {
                locator.GrossDemandId,
                locator.RootNetDemandId,
                Active = SwiftExport.Contracts.SysEnums.NetDemandLifecycleStatusEnum.Active
            }, Transaction);
        return [.. rows];
    }

    private sealed class FamilyLocator
    {
        public Guid GrossDemandId { get; set; }
        public Guid RootNetDemandId { get; set; }
    }

}

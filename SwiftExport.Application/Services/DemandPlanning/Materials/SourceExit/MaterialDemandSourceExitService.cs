using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.SourceExit;

/// <summary>在来源调用方事务内锁定物料事实，提供活动和退出历史证明。</summary>
public sealed class MaterialDemandSourceExitService(ServiceBaseContext context, INetDemandRepository netDemandRepository) : IMaterialDemandSourceExitService
{
    private readonly DbSession _session = context.Session;

    /// <summary>在既有事务内按来源需求锁定活动物料行，供来源本体写操作做边界校验。</summary>
    public async Task<bool> HasActiveAsync(Guid grossDemandId)
    {
        if (_session.Transaction is null)
            throw new InvalidOperationException("物料事实校验必须在调用方事务内执行。");
        return (await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(grossDemandId)).Count > 0;
    }

    /// <summary>
    /// 在来源调用方事务内批量取得物料退出证明；评估只解释 NetDemand 历史，不重复扫描执行和库存事实。
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> AssessSourceExitAsync(
        IEnumerable<Guid> grossDemandIds)
    {
        if (_session.Transaction is null)
            throw new InvalidOperationException("来源退出校验必须在调用方事务内执行。");
        var ids = grossDemandIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, MaterialDemandSourceExitAssessment>();
        var rows = await netDemandRepository.AssessSourceExitForUpdateAsync(ids);
        return rows.ToDictionary(row => row.GrossDemandId);
    }

}

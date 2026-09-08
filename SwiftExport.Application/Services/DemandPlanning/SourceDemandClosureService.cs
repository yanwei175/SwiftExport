using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.DemandPlanning;
namespace SwiftExport.Application.Services.DemandPlanning;
/// <summary>检查来源行及正式交付净额，避免将物料已拆解误认为已完成。</summary>
public sealed class SourceDemandClosureService(ServiceBaseContext context,ISourceDemandClosureRepository history,IGrossDemandRepository demands) : ISourceDemandClosureService
{
    /// <inheritdoc />
    public async Task VerifyAsync(string sourceType,Guid sourceUid,IReadOnlyDictionary<Guid,decimal> expectedLines,CancellationToken cancellationToken=default)
    {
        if(context.Session.Transaction is null) throw new InvalidOperationException("结案复核要求调用方事务。");
        var rows=await history.GetForUpdateAsync(sourceType,sourceUid);
        // 已取消确认留下的作废版本不是当前要求，也不能作为合法完成的证明。
        var current=rows.Where(x=>x.LifecycleStatus!=GrossDemandLifecycleStatusEnum.Voided).ToList();
        if(current.Count!=expectedLines.Count || current.GroupBy(x=>x.SourceLineId).Any(x=>x.Count()!=1)
            || current.Any(x=>!expectedLines.ContainsKey(x.SourceLineId)))
            throw new SourceDemandClosureException("SOURCE_MISMATCH","来源记录缺失、重复或存在额外要求，不能结案。");
        var delivery=(await demands.GetDeliveryContextsForUpdateAsync(current.Select(x=>x.Uid).OrderBy(x=>x),cancellationToken)).ToDictionary(x=>x.GrossDemandId);
        foreach(var row in current)
        {
            if(row.DemandQty!=expectedLines[row.SourceLineId] || !delivery.TryGetValue(row.Uid,out var delivered)
                || delivered.DeliveredQty<0 || delivered.DeliveredQty>row.DemandQty)
                throw new SourceDemandClosureException("SOURCE_MISMATCH","来源数量或交付事实异常，不能结案。");
            if(row.LifecycleStatus!=GrossDemandLifecycleStatusEnum.Closed && delivered.DeliveredQty!=row.DemandQty)
                throw new SourceDemandClosureException("SOURCE_NOT_COMPLETED","仍有未交付要求，请先到下游完成或合法终止。");
        }
    }
}

using SwiftExport.Core.Entities.DemandPlanning;
namespace SwiftExport.Core.Interfaces.DemandPlanning;
/// <summary>来源结案检查专用历史读取，包含已关闭来源而不依赖活动筛选。</summary>
public interface ISourceDemandClosureRepository
{
    /// <summary>按来源加锁读取未删除历史，供当前事实和交付净额核验。</summary>
    Task<IReadOnlyList<GrossDemand>> GetForUpdateAsync(string sourceType, Guid sourceUid);
}

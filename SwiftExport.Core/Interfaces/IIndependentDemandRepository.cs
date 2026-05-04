using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface IIndependentDemandRepository : IRepository<IndependentDemand>
{

    /// <summary>
    /// 根据SourceType和SourceId列表获取IndependentDemand列表
    /// </summary>
    /// <param name="sourceType">来源单据类型</param>
    /// <param name="sourceIds">来源单据ID列表</param>
    /// <returns></returns>
    Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceIdAsync(string sourceType, IEnumerable<int> sourceIds);

    /// <summary>
    /// 根据SourceType和SourceId删除单据记录
    /// </summary>
    /// <param name="sourceType">来源单据类型</param>
    /// <param name="sourceIds">来源单据ID列表</param>
    /// <returns></returns>
    Task<bool> DeleteRangeBySourceTypeAndSourceIdListAsync(string sourceType, IEnumerable<int> sourceIds);

    /// <summary>
    /// 根据来源类型和来源单号获取销售需求列表
    /// </summary>
    /// <param name="sourceType">来源单据类型</param>
    /// <param name="sourceNo">来源单号</param>
    /// <returns>销售需求列表</returns>
    Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceNoAsync(string sourceType, string sourceNo);

    /// <summary>
    /// 根据来源类型和来源行ID列表获取销售需求列表
    /// </summary>
    /// <param name="sourceType">来源单据类型</param>
    /// <param name="sourceLineIds">来源行ID列表</param>
    /// <returns>销售需求列表</returns>
    Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceLineIdsAsync(string sourceType, IEnumerable<int> sourceLineIds);

    /// <summary>
    /// 批量更新销售需求的已填充数量并按规则推进状态。
    /// </summary>
    /// <param name="sourceTypeLineQtyMap">来源类型 -> 来源行ID与增量数量映射</param>
    /// <returns>受影响行数</returns>
    Task<int> BatchIncreaseFilledQtyAsync(IDictionary<string, IDictionary<int, decimal>> sourceTypeLineQtyMap);

    /// <summary>
    /// 并发防重：尝试将指定来源行标记为已拆解。
    /// </summary>
    /// <param name="sourceType">来源类型</param>
    /// <param name="sourceNo">来源单号</param>
    /// <param name="sourceLineIds">来源行ID列表</param>
    /// <param name="decomposedTime">拆解时间</param>
    /// <returns>更新成功行数</returns>
    Task<int> TryMarkDecomposedAsync(string sourceType, string sourceNo, IEnumerable<int> sourceLineIds, DateTime decomposedTime);

    /// <summary>
    /// 拆解失败回滚：将指定来源行恢复为未拆解。
    /// </summary>
    /// <param name="sourceType">来源类型</param>
    /// <param name="sourceNo">来源单号</param>
    /// <param name="sourceLineIds">来源行ID列表</param>
    /// <returns>更新成功行数</returns>
    Task<int> RollbackDecomposedMarkAsync(string sourceType, string sourceNo, IEnumerable<int> sourceLineIds);


}

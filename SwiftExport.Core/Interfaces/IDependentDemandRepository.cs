using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 销售订单拆解中间表仓储接口。
    /// </summary>
    public interface IDependentDemandRepository : IRepository<DependentDemand>
    {
        /// <summary>
        /// 根据来源类型与来源单号查询中间表记录。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetBySourceSoNoAsync(string sourceType, string sourceSoNo);

        /// <summary>
        /// 根据来源类型与来源销售单行 Id 查询中间表记录（可能多行）。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetListBySourceSoLineIdAsync(string sourceType, long sourceSoLineId);

        /// <summary>
        /// 根据来源类型与来源单号查询需求池记录。
        /// </summary>
        Task<IReadOnlyList<DependentDemand>> GetBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 删除来源类型与来源单号对应的需求池记录。
        /// </summary>
        Task<int> DeleteBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按业务维度分页查询（用于管理页）。
        /// </summary>
       new Task<PagedResult<DependentDemand>> GetPagedListAsync(
            QueryCriteria criteria,
            int pageIndex,
            int pageSize,
            string orderBy = "Id DESC");
    }
}

using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 销售订单拆解中间表仓储接口。
    /// </summary>
    public interface IRequirementPoolRepository : IRepository<RequirementPool>
    {
        /// <summary>
        /// 根据来源销售单号查询中间表记录。
        /// </summary>
        Task<IReadOnlyList<RequirementPool>> GetBySourceSoNoAsync(string sourceSoNo);

        /// <summary>
        /// 根据来源销售单行 Id 查询中间表记录。
        /// </summary>
        Task<RequirementPool?> GetBySourceSoLineIdAsync(long sourceSoLineId);

        /// <summary>
        /// 根据来源类型与来源单号查询需求池记录。
        /// </summary>
        Task<IReadOnlyList<RequirementPool>> GetBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 删除来源类型与来源单号对应的需求池记录。
        /// </summary>
        Task<int> DeleteBySourceAsync(string sourceType, string sourceNo);

        /// <summary>
        /// 按业务维度分页查询（用于管理页）。
        /// </summary>
       new Task<PagedResult<RequirementPool>> GetPagedListAsync(
            QueryCriteria criteria,
            int pageIndex,
            int pageSize,
            string orderBy = "Id DESC");
    }
}

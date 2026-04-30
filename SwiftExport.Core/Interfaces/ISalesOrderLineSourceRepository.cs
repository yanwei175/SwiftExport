using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 销售订单行数据源仓储接口。
    /// 负责 CustomerProduct + Product 联查分页。
    /// </summary>
    public interface ISalesOrderLineSourceRepository
    {
        /// <summary>
        /// 联查分页：返回可直接映射为 SalesOrderLineDto 的读模型。
        /// </summary>
        Task<PagedResult<SalesOrderLineDto>> GetPagedSalesOrderLineSourceDtoAsync(
            QueryCriteria criteria,
            int pageIndex,
            int pageSize,
            string orderBy = "cp.Id DESC");
    }
}

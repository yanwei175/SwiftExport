using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 销售订单行数据源服务接口。
    /// 提供基于 CustomerProduct + Product 联查的分页查询能力，
    /// 供销售订单行快速选型、带出价格与描述等信息。
    /// </summary>
    public interface ISalesOrderLineService
    {
        Task<ServiceResult<List<SalesOrderLineDto>>> GetLineListByHeadUidAsync(Guid headUid);

    }
}

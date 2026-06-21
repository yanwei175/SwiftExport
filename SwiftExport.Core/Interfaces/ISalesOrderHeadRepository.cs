using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
    {
        Task<PagedResultDto<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchDto req);
        Task<SalesOrderHeaderManagerDto?> GetSalesOrderHeaderManagerByIdAsync(Guid orderId);
        Task<List<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerListByIdListAsync(List<Guid> uidList);
        Task<bool> DeleteSalesOrderAsync(Guid orderId, int version);
    }
}

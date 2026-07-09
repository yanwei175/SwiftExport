using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
    {
        Task<PagedResponseBase<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchRequest req);
        Task<SalesOrderHeaderManagerDto?> GetSalesOrderHeaderManagerByIdAsync(Guid orderId);
        Task<List<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerListByIdListAsync(List<Guid> uidList);
        Task<bool> DeleteSalesOrderAsync(Guid orderId, int version);
    }
}

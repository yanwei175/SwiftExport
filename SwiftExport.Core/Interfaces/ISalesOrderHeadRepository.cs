using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Interfaces
{
    public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
    {
        Task<SalesOrderHeaderManagerDto?> GetSalesOrderHeaderManagerByIdAsync(Guid orderId);
        Task<PagedResultDto<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchDto req);
    }
}

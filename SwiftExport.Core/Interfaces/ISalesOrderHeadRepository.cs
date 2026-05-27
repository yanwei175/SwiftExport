using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Interfaces
{
    public interface ISalesOrderHeadRepository
    {
        Task<PagedResultDto<SalesOrderHeaderDto>> GetSalesOrderHeaderPagedListAsync(SalesOrderManagerSearchDto request, string orderBy = "h.CreatedDate DESC");
    }
}

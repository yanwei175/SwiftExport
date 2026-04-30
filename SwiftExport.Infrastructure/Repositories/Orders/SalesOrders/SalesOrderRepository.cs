using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders.SalesOrders
{
    public class SalesOrderRepository(RepositoryBaseContext context) :DapperRepository<SalesOrderHeader>(context), ISalesOrderRepository
    {

        //public async Task<ServiceResult<int>> SaveSalesOrderDetailAsync(SalesOrderDetailDto request)
        //{
        //    return null;
        //}





    }
}

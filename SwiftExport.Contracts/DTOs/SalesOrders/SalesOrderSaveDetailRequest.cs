using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderSaveDetailRequest : SaveBillRequest<SalesOrderHeaderDto, SalesOrderLineDto>
    {
    }
}

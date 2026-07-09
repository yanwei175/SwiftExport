using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.PurchaseOrders.Requests
{
    /// <summary>
    /// 采购订单分页查询条件。
    /// </summary>
    public class PurchaseOrderSearchRequest : PagedRequestBase
    {

        public string OrderNo { get; set; }
        public string PoNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string SupplierCode { get; set; }
        public int OrderStatus { get; set; }

    }
}

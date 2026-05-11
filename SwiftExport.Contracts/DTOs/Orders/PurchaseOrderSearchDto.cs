namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购订单分页查询条件。
    /// </summary>
    public class PurchaseOrderSearchDto
    {
        public string Keyword { get; set; }
        public string OrderNo { get; set; }
        public string PoNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string SupplierCode { get; set; }
        public int OrderStatus { get; set; }

        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}

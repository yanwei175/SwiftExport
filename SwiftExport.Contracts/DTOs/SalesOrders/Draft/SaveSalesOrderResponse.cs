namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单保存成功后返回的最新详情。</summary>
    public class SaveSalesOrderResponse
    {
        public SalesOrderDetailDto Document { get; set; }
    }
}

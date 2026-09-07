using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单详情选择客户时使用的业务默认值快照。</summary>
    /// <remarks>来源表：Customer。</remarks>
    public class SalesOrderCustomerLookupDto
    {
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string Currency { get; set; }
        public string PaymentMethod { get; set; }
        public string ShippedBy { get; set; }
        public string Incoterm { get; set; }
        public string IncotermSortRemark { get; set; }
        public string IncotermLocation { get; set; }
        public decimal DepositRate { get; set; }
        public int LeadTimeDays { get; set; }
        public string ShipToName { get; set; }
        public string ShipToAddress { get; set; }
        public string ShipToPhone { get; set; }
    }
}

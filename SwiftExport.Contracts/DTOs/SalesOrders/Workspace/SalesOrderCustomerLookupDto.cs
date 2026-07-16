using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
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

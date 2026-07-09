using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;

namespace SwiftExport.View.FW.PurchaseOrderViews.ViewModels
{
    internal class PurchaseOrderSearchDto : PurchaseOrderSearchRequest
    {
        public string SupplierCode { get; set; }
        public string PoNo { get; set; }
        public string SalesOrderNo { get; set; }
        public int OrderStatus { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
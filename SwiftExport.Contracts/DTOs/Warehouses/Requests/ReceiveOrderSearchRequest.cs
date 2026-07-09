using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Warehouses.Requests
{
    public class ReceiveOrderSearchRequest
    {
        public string Keyword { get; set; }
        public List<string> BatchKeywords { get; set; }//p批量搜索
        public string ReceiveNo { get; set; }
        public string PoNo { get; set; }
        public string SupplierCode { get; set; }
        public int OrderStatus { get; set; }
        public DateTime? ReceiveDateFrom { get; set; }
        public DateTime? ReceiveDateTo { get; set; }
        public bool IsPosted { get; set; } = false;
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}

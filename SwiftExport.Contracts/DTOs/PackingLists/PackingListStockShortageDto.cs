using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.PackingLists
{
    public class PackingListStockShortageDto
    {
        public string Sku { get; set; }
        public string SupplierCode { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OnhandQty { get; set; }
        public decimal ShortageQty { get; set; }
    }
}

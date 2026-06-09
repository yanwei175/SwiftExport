using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderLineManagerDto : BaseDto
    {
        public string MainImage { get; set; }

        public string CustomerSku { get; set; }

        public string Sku { get; set; }

        public string DescriptionEn { get; set; }

        public string ColorNameEn { get; set; }

        public decimal Qty { get; set; }

        public decimal? ShippedQty { get; set; }

        public decimal? UnshippedQty { get; set; }

        public decimal? UsdPrice { get; set; }

        public decimal? CnyPrice { get; set; }

        public decimal? UsdAmount { get; set; }

        public decimal? CnyAmount { get; set; }

        public string SubNote { get; set; }

    }
}

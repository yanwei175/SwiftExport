using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class SupplierProducts : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string ProductSKU { get; set; }
        public string SupplierCode { get; set; }
        public string SSKU { get; set; }
        public string SDescription { get; set; }
        public string SFeatures { get; set; }
        public decimal RMBPrice1 { get; set; }
        public decimal RMBPrice2 { get; set; }

    }
}

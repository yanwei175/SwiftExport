using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class BOM : BaseEntity

    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string BomNo { get; set; }
        public string ProductSKU { get; set; }
        public decimal Qty { get; set; }
        public decimal SkuID {  get; set; }
        public string SubType { get; set; }
    }
}

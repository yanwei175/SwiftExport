using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class CustomerProducts : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string CustomerCode { get; set; }
        public string CSku { get; set; }
        public string LinkSku { get; set; }
        public string ProductSKU { get; set; }
        public string CSeries { get; set; }
        public string CDescription { get; set; }
        public string CFinish { get; set; }
        public string CFeatures { get; set; }
        public string SpecFeatures { get; set; }
        public decimal RMBPrice { get; set; }
        public decimal USDPrice { get; set; }
        public string Barcode1 { get; set; }
        public string Barcode2 { get; set; }
        public string Note { get; set; }

    }
}

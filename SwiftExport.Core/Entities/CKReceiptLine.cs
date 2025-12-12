using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class CKReceiptLine : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string PoNo { get; set; }
        public long PoLineID { get; set; }
        public string SKU { get; set; }
        public string SupplierCode { get; set; }
        public decimal ReceiveQTY { get; set; }
        public string Note { get; set; }

    }
}

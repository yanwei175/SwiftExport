using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class PlanOrderLine : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string PlanOrderNo { get; set; }
        public string SoNo { get; set; }
        public long SoLineID { get; set; }
        public string SKU { get; set; }
        public decimal QTY { get; set; }
        public string DeliveryTo { get; set; }
        public string CustomerCode { get; set; }
        public string Note { get; set; }

        public string SupplierCode { get; set; }
    }
}

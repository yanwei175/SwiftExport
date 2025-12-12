using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class PurchaseOrderLine : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string PoNo { get; set; }
        public string SKU { get; set; }
        public string CSku { get; set; }
        public string LinkSku { get; set; }
        public string SSKU { get; set; }
        public string Description { get; set; }
        public string Finish { get; set; }
        public string Features { get; set; }
        public decimal RMBPrice1 { get; set; }
        public decimal RMBPrice2 { get; set; }
        public decimal QTY { get; set; }
        public decimal RMB1Amount { get; set; }
        public decimal RMB2Amount { get; set; }
        public string SubNote { get; set; }
        public string SupplyType { get; set; }
        public string DeliveryTo { get; set; }

        public long SoLineID { get; set; }
        public decimal RecQTY { get; set; }
        public DateTime LastRecDate { get; set; }
        public string FollowUpRemarks { get; set; }
        public DateTime FollowUpTime { get; set; }
        public DateTime FollowExpectedDate { get; set; }
    }
}

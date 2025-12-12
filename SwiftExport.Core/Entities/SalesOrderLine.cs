using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class SalesOrderLine : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string SoNo { get; set; }
        public string SKU { get; set; }
        public string CSku { get; set; }
        public string LinkSku { get; set; }
        public string CDescription { get; set; }
        public string CFinish { get; set; }
        public string CFeatures { get; set; }
        public decimal RMBPrice { get; set; }
        public decimal USDPrice { get; set; }
        public decimal QTY { get; set; }
        public decimal RMBAmount { get; set; }
        public decimal USDAmount { get; set; }
        public decimal PcsOfCarton { get; set; }
        public decimal NwOfCarton { get; set; }
        public decimal GwOfCarton { get; set; }
        public decimal CbmOfCarton { get; set; }
        public decimal PcsSubTotal { get; set; }
        public decimal NwSubTotal { get; set; }
        public decimal GwSubTotal { get; set; }
        public decimal CbmSubTotal { get; set; }
        public string SubNote { get; set; }

        public decimal ShippedQTY { get; set; }
        public DateTime LastShippedDate { get; set; }
        public DateTime ExpectedDeliveryDate { get; set; }
    }
}

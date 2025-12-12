using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class CKPackingListHead : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string PLNo { get; set; }
        public DateTime PLDate { get; set; }
        public string CustomerCode { get; set; }
        public string ShipTo { get; set; }
        public string SoNo { get; set; }
        public DateTime SoDate { get; set; }
        public string TransportNo { get; set; }
        public string SealNo { get; set; }
        public DateTime ETD { get; set; }
        public DateTime ETA { get; set; }
        public string Attention { get; set; }
        public string PLFolder { get; set; }
    }
}

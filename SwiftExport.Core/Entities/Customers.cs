using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class Customers : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerAddress { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerContact { get; set; }
        public string LabelFolder { get; set; }
        public string IMFolder { get; set; }
        public string DrawingFolder { get; set; }
        public string LinedrawingFolder { get; set; }
        public string BarcodeFolder { get; set; }
        public string SpecSheetFolder { get; set; }

    }
}

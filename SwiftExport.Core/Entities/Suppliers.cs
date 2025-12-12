using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class Suppliers : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public decimal ZheKou { get; set; }
        public string ZheKouBeiZhu { get; set; }
    }
}

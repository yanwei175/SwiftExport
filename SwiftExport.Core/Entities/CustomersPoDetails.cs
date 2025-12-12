using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class CustomersPoDetails : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string CustomerCode { get; set; }
        public string SupplierCode { get; set; }
        public string InletrHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string IMType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string SoPerson { get; set; }
        public string PoPerson { get; set; }
        public string Note { get; set; }

    }
}

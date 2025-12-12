using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class PurchaseOrderHead : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string PoNo { get; set; }
        public string SoNo { get; set; }
        public string CustomerPoNo { get; set; }
        public DateTime PoDate { get; set; }
        public DateTime ExpectedDate { get; set; }
        public DateTime RePlyDate { get; set; }
        public string CustomerCode { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string SoPerson { get; set; }
        public string PoPerson { get; set; }
        public string InletrHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string IMType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string PoFolder { get; set; }
        public string PoNote { get; set; }
        public string CreatedBy { get; set; }
        public string ReviewedBy { get; set; }
        public string ApprovedBy { get; set; }

        public decimal ZhiJieYouHuiJinE { get; set; }
        public decimal ZheKou { get; set; }
        public string ZheKouBeiZhu { get; set; }
    }
}

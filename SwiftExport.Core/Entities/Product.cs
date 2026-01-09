using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities
{
    public class Product : BaseEntity

    {
        public string MasterSku { get; set; } // 逻辑外键：直接存字符串 "AT010011"
        public string Even { get; set; }
        public string Status { get; set; }
        public string SKU { get; set; }
        public string Description { get; set; }
        public string Finish { get; set; }
        public string Features { get; set; }
        public string SupplyType { get; set; }
        public string BomNo { get; set; }
        public string Class { get; set; }
        public string Note { get; set; }
        public string LinkSku { get; set; }
        public string MainImage { get; set; }
        public string CustomerCode { get; set; }
        public string CSKU { get; set; }
        public string SupplierCode { get; set; }
        public string SSKU { get; set; }


    }
}

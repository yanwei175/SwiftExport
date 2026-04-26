using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Suppliers
{
    public class SupplierDto : BaseDto
    {
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public decimal ZheKou { get; set; }
        public string ZheKouBeiZhu { get; set; }
        public string DocRelateNo { get; set; }
    }
}

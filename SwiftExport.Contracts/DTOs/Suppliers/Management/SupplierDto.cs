using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Suppliers
{
    public class SupplierDto : BaseDto
    {
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public decimal? ZheKou { get; set; }
        public string ZheKouBeiZhu { get; set; } = string.Empty;
        public string DocRelateNo { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Currency { get; set; } = "CNY";
        public decimal? TaxRate { get; set; }
        public string PaymentTerms { get; set; } = string.Empty;
        public int? DefaultLeadTime { get; set; }
    }
}

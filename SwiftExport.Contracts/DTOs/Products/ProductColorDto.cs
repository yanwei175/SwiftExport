using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class ProductColorDto : BaseDto
    {
        public string ColorCodeUnique { get; set; }
        public string ColorCode { get; set; }
        public string ColorNameEn { get; set; }
        public string ColorNameZh { get; set; }
    }
}

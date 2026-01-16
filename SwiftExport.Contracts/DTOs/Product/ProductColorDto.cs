using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Product
{
    public class ProductColorDto : BaseDto
    {

        public string ColorName { get; set; }
        public string ColorCode { get; set; }
    }
}

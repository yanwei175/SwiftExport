using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class ProductCategoryDto : BaseDto
    {
        public string CategoryName { get; set; }
        public int? ParentId { get; set; }
        public string FakeName { get; set; }
        public string CategoryCode { get; set; }
        public int SortNo { get; set; }
    }
}
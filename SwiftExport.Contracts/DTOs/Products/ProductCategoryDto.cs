using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class ProductCategoryDto : BaseDto
    {
        public string CategoryName { get; set; }
        public Guid? ParentId { get; set; }
        public string FakeName { get; set; }
        public string CategoryCode { get; set; }
        public int SortNo { get; set; }
    }
}
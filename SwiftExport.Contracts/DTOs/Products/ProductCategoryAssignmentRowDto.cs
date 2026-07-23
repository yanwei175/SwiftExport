using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>产品快速归类列表的轻量只读行。</summary>
    public class ProductCategoryAssignmentRowDto : BaseDto
    {
        public string MainImage { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string DescriptionZh { get; set; }
        public Guid OriginalCategoryId { get; set; }
        public string OriginalCategoryName { get; set; }
    }
}

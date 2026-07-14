using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>
    /// 产品搜索选择器使用的轻量只读模型。
    /// </summary>
    public class ProductLookupDto
    {
        public Guid Uid { get; set; }
        public string Sku { get; set; }
        public string DescriptionEn { get; set; }
        public string DescriptionZh { get; set; }
        public string LinkSku { get; set; }
        public string CategoryName { get; set; }
        public string MainImage { get; set; }
        public string Finish { get; set; }
    }
}

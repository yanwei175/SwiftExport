using System;

namespace SwiftExport.View.FW.SalesOrderViews.Demo
{
    /// <summary>
    /// Demo 专用产品候选项，不代表真实业务 DTO。
    /// </summary>
    public class ProductLookupDemoItem
    {
        public Guid ProductId { get; set; }
        public string CompanyModel { get; set; } = string.Empty;
        public string DescriptionEn { get; set; } = string.Empty;
        public string DescriptionZh { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
    }
}

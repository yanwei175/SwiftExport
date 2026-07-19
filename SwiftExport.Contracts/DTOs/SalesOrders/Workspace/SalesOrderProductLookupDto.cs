using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    /// <summary>
    /// 销售订单行选择标准产品时使用的客户化查询结果。
    /// </summary>
    /// <remarks>来源表：Product、ProductColor、CustomerProduct、ProductPackage。</remarks>
    public class SalesOrderProductLookupDto
    {
        public Guid ProductId { get; set; }
        public Guid? CustomerProductId { get; set; }
        public string CustomerSku { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string DescriptionEn { get; set; }
        public string ColorNameEn { get; set; }
        public string FeaturesEn { get; set; }
        public string MainImage { get; set; }
        public decimal? CnyPrice { get; set; }
        public decimal? UsdPrice { get; set; }
    }
}

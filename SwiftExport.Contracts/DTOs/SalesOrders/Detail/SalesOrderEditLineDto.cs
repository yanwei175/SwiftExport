using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单详情保存命令中的可编辑行数据。</summary>
    public class SalesOrderEditLineDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public bool IsNonStandard { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? CustomerProductId { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string DescriptionEn { get; set; }
        public string ColorNameEn { get; set; }
        public string FeaturesEn { get; set; }
        public decimal Qty { get; set; }
        public decimal? CnyPrice { get; set; }
        public decimal? UsdPrice { get; set; }
        public decimal? PcsOfCarton { get; set; }
        public decimal? NwOfCarton { get; set; }
        public decimal? GwOfCarton { get; set; }
        public decimal? CbmOfCarton { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string SubNote { get; set; }
    }
}

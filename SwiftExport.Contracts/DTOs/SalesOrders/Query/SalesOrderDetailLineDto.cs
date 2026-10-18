using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>
    /// 订单详情行事实及采用时商业快照，不查询成品需求或交付进度。
    /// </summary>
    public class SalesOrderDetailLineDto : BaseDto
    {
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
        /// <summary>销售行订购数量使用的计量单位代码快照。</summary>
        public string UnitSnapshot { get; set; }
        public decimal? CnyPrice { get; set; }
        public decimal? UsdPrice { get; set; }
        public decimal? PcsOfCarton { get; set; }
        public decimal? NwOfCarton { get; set; }
        public decimal? GwOfCarton { get; set; }
        public decimal? CbmOfCarton { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string SubNote { get; set; }
        public string MainImage { get; set; }
        public decimal? CnyAmount { get; set; }
        public decimal? UsdAmount { get; set; }
    }
}

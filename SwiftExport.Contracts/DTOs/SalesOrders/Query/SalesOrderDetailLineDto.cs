using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>
    /// 详情查询行；附加字段只用于显示，不进入保存命令。
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
        /// <summary>按可信来源及有效已过账装箱行核实的正式已交数量；无法核实时为空。</summary>
        public decimal? ShippedQty { get; set; }

        /// <summary>商业未交数量；超发时为零，无法核实时为空。</summary>
        public decimal? UnshippedQty { get; set; }
        /// <summary>该行来源或交付历史需要人工核实的原因。</summary>
        public string ReviewReason { get; set; }
        public decimal? CnyAmount { get; set; }
        public decimal? UsdAmount { get; set; }
    }
}

using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 销售订单行明细 DTO（数据传输对象）。
    /// 用于前后端交互，代表订单中的单个产品项。
    /// </summary>
    public class SalesOrderLineDto : BaseDto
    {
        public int LineNumber { get; set; }
        public Guid SalesOrderId { get; set; }
        public string OrderNo { get; set; }

        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string MaterialType { get; set; }

        /// <summary>
        /// 客户产品主键 Id（来源于 CustomerProduct.Id，用于前端后续联动或回填时定位）。
        /// </summary>
        public Guid? CustomerProductId { get; set; }

        /// <summary>
        /// 产品主键 Id（来源于 Product.Id）。
        /// </summary>
        public Guid? ProductId { get; set; }

        public string CustomerSku { get; set; }

        /// <summary>
        /// 产品英文描述（对应 Product.DescriptionEn）。
        /// </summary>
        public string DescriptionEn { get; set; }

        /// <summary>
        /// 产品英文颜色名（对应 ProductColor.ColorNameEn）。
        /// </summary>
        public string ColorNameEn { get; set; }

        /// <summary>
        /// 产品英文特性（对应 Product.FeaturesEn）。
        /// </summary>
        public string FeaturesEn { get; set; }

        /// <summary>
        /// 人民币单价（字段命名统一为 CNY）。
        /// </summary>
        public decimal? CnyPrice { get; set; }
        public decimal? UsdPrice { get; set; }
        public decimal? Qty { get; set; }

        /// <summary>
        /// 人民币金额（字段命名统一为 CNY）。
        /// </summary>
        public decimal? CnyAmount { get; set; }
        public decimal? UsdAmount { get; set; }

        public decimal? PcsOfCarton { get; set; }
        public decimal? NwOfCarton { get; set; }
        public decimal? GwOfCarton { get; set; }
        public decimal? CbmOfCarton { get; set; }

        public decimal? Ctns { get; set; }
        public decimal? NwSubTotal { get; set; }
        public decimal? GwSubTotal { get; set; }
        public decimal? CbmSubTotal { get; set; }

        public string SubNote { get; set; }
        public DateTime? LastShippedDate { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string MainImage { get; set; }
    }
}

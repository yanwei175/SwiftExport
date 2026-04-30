using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购订单行 DTO。
    /// </summary>
    public class PurchaseOrderLineDto : BaseDto
    {
        public string Even { get; set; }
        public string Status { get; set; }

        public int? PurchaseOrderId { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string CustomerSku { get; set; }
        public string LinkSku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }

        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal QTY { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }

        public string SubNote { get; set; }
        /// <summary>
        /// 供货策略（拆解策略）。
        /// </summary>
        public string SupplyType { get; set; }

        /// <summary>
        /// 物料分类（MaterialTypeEnums 字符串值）。
        /// </summary>
        public string MaterialType { get; set; }

        /// <summary>
        /// 单件体积（CBM）。
        /// </summary>
        public decimal? UnitCBM { get; set; }

        /// <summary>
        /// 单件重量（KG）。
        /// </summary>
        public decimal? UnitWeight { get; set; }

        /// <summary>
        /// 供应商反馈完工数量。
        /// </summary>
        public decimal ReadyQty { get; set; }

        public string DeliveryTo { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? ChangeTime { get; set; }

        /// <summary>
        /// 来源销售行ID（回写需求池关键字段）。
        /// </summary>
        public long? SoLineId { get; set; }

        /// <summary>
        /// 财务实收数量。
        /// </summary>
        public decimal RecQty { get; set; }
        public DateTime? LastRecDate { get; set; }
        public string FollowUpRemarks { get; set; }
        public DateTime? FollowUpTime { get; set; }
        public DateTime? FollowExpectedDate { get; set; }
    }
}

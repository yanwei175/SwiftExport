using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.PurchaseOrders
{
    /// <summary>
    /// 采购订单行 DTO。
    /// </summary>
    public class PurchaseOrderLineDto : BaseDto
    {
        /// <summary>
        /// 行状态（业务状态枚举值）。
        /// </summary>
        public int ReadyStatus { get; set; }

        /// <summary>
        /// 行号。
        /// </summary>
        public int LineNumber { get; set; }

        public Guid? PurchaseOrderId { get; set; }
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
        /// 物料分类（MaterialTypesEnum 字符串值）。
        /// </summary>
        public string MaterialType { get; set; }

        /// <summary>
        /// 供应商反馈完工数量。
        /// </summary>
        public decimal ReadyQty { get; set; }

        public string DeliveryTo { get; set; }


        /// <summary>
        /// 冗余字段，便于追溯来源。
        /// </summary>
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// 冗余字段，便于追溯来源。
        /// </summary>
        public string SourceNo { get; set; } = string.Empty;

        /// <summary>
        /// 财务实收数量。
        /// </summary>
        public decimal RecQty { get; set; }
        public DateTime? LastRecDate { get; set; }
        public string FollowUpRemarks { get; set; }
        public DateTime? FollowUpTime { get; set; }
        public DateTime? FollowExpectedDate { get; set; }
        public string MainImage { get; set; }=string.Empty;


        /// <summary>
        /// 手动创建或修改时，关联的净需求 ID 集合
        /// </summary>
        public List<Guid> DemandIdList { get; set; } = new List<Guid>();
    }
}

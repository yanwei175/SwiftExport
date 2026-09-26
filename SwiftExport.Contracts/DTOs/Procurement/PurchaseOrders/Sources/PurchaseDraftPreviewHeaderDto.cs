using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购创建预览中的可信只读单头快照；新建时由来源与客户采购配置组成，追加时来自既有采购草稿。</summary>
    public class PurchaseDraftPreviewHeaderDto
    {
        /// <summary>已有采购单号；新建预览为空，表示沿用现有创建阶段的编号生成时点。</summary>
        public string OrderNo { get; set; }
        /// <summary>客户采购单号，销售来源时取销售订单头。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>采购业务关联号；新建销售来源按已确认的三段值直接拼接。</summary>
        public string BoNo { get; set; }
        /// <summary>采购单币种快照；未配置时保持为空，不做币种猜测或换算。</summary>
        public string Currency { get; set; }
        /// <summary>价格倍率快照；仅展示和保存，不参与行价或折扣计算。</summary>
        public decimal? DiscountRate { get; set; }
        /// <summary>整单人工折扣金额；创建预览未提供时保持空值。</summary>
        public decimal? DiscountAmount { get; set; }
        /// <summary>折扣说明快照。</summary>
        public string DiscountRemark { get; set; }
        /// <summary>按采购行未税金额求和的预览合计。</summary>
        public decimal UntaxedTotalAmount { get; set; }
        /// <summary>按采购行含税金额求和的预览合计。</summary>
        public decimal TaxIncludedTotalAmount { get; set; }
        /// <summary>已有草稿的下单日期；新建预览为空，由创建阶段沿用现有系统时间规则。</summary>
        public DateTime? OrderDate { get; set; }
        /// <summary>当前单据所有采购行的最早计划交期。</summary>
        public DateTime? ExpectedDate { get; set; }
        /// <summary>进水管要求快照。</summary>
        public string InletHoseType { get; set; }
        /// <summary>起泡器要求快照。</summary>
        public string AeratorType { get; set; }
        /// <summary>托盘要求快照。</summary>
        public string PalletType { get; set; }
        /// <summary>内盒要求快照。</summary>
        public string BoxType { get; set; }
        /// <summary>标贴要求快照。</summary>
        public string LabelType { get; set; }
        /// <summary>说明书要求快照。</summary>
        public string InstructionManualType { get; set; }
        /// <summary>外箱要求快照。</summary>
        public string CartonType { get; set; }
        /// <summary>Logo 要求快照。</summary>
        public string LogoType { get; set; }
        /// <summary>销售经办人快照。</summary>
        public string SalesPerson { get; set; }
        /// <summary>采购经办人快照。</summary>
        public string PurchasePerson { get; set; }
        /// <summary>采购特殊要求或业务备注快照。</summary>
        public string Remark { get; set; }
    }
}

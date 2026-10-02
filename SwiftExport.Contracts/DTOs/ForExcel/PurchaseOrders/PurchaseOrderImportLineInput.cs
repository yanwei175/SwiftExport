using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>已解析采购行；检查不覆盖原报价和商业文本。</summary>
    public sealed class PurchaseOrderImportLineInput
    {
        /// <summary>页面稳定行标识，仅用于结果对齐。</summary>
        public Guid ClientLineId { get; set; }
        /// <summary>用户明确标记非标，不能由匹配失败自动推断。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>歧义时显式选择的客户产品关联。</summary>
        public Guid? SelectedCustomerProductId { get; set; }
        /// <summary>用于精确匹配的客户型号。</summary>
        public string CustomerSku { get; set; }
        /// <summary>Excel 本司型号，即中性型号快照。</summary>
        public string LinkSku { get; set; }
        /// <summary>用户已确认中性型号差异；保存时仍重新比较。</summary>
        public bool AcceptLinkSkuMismatch { get; set; }
        /// <summary>非标单位；标准行单位由主数据确定。</summary>
        public string Unit { get; set; }
        /// <summary>表面处理快照。</summary>
        public string FinishZh { get; set; }
        /// <summary>配置快照。</summary>
        public string FeaturesZh { get; set; }
        /// <summary>采购数量。</summary>
        public decimal Quantity { get; set; }
        /// <summary>未税单价；必须明确填写，零价有效。</summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>可选含税单价。</summary>
        public decimal? TaxPrice { get; set; }
        /// <summary>客供件实际送达供应商。</summary>
        public Guid? DeliverySupplierId { get; set; }
        /// <summary>行计划交期。</summary>
        public DateTime? PlannedDeliveryDate { get; set; }
        /// <summary>工厂型号快照。</summary>
        public string SupplierSku { get; set; }
        /// <summary>中文描述快照，非标必填。</summary>
        public string DescriptionZh { get; set; }
        /// <summary>行备注。</summary>
        public string SubNote { get; set; }
    }
}


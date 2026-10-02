using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>手工或导入采购行；标准身份由服务端读取，非标必须明确标记。</summary>
    public sealed class CreateIndependentPurchaseOrderLineCommand
    {
        public Guid ProductId { get; set; }
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

        public decimal Quantity { get; set; }
        /// <summary>客供件实际送达供应商；普通物料保持为空。</summary>
        public Guid? DeliverySupplierId { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string SubNote { get; set; }
    }
}

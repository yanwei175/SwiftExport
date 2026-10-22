using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>手工或导入采购行；标准身份由服务端读取，非标必须明确标记。</summary>
    public sealed class CreateIndependentPurchaseOrderLineCommand
    {
        /// <summary>标准采购的真实产品身份；手工选择直接提交此身份，服务端重新读取产品及关系快照。</summary>
        public Guid ProductId { get; set; }
        /// <summary>用户明确标记非标，不能由匹配失败自动推断。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>按客户型号导入时，用于消解歧义的客户产品关联；手工直接选择产品时保持空值。</summary>
        public Guid? SelectedCustomerProductId { get; set; }
        /// <summary>导入时用于精确匹配的客户型号；手工直接选择产品时保持 null，由服务端读取快照。空字符串不是省略匹配。</summary>
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
        /// <summary>未税单价，必须明确填写，允许零价，非负且最多四位小数；空值表示未填写，由服务端拒绝。</summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>含税单价，必须明确填写，允许零价，非负且最多四位小数；不由未税单价自动推算。</summary>
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string SubNote { get; set; }
    }
}

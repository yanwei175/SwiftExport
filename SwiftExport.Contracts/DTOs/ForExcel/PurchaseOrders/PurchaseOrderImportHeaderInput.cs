using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>已解析采购单头，仅表达商业资料，不包含来源和审计身份。</summary>
    public sealed class PurchaseOrderImportHeaderInput
    {
        /// <summary>采购订单日期。</summary>
        public DateTime? OrderDate { get; set; }
        /// <summary>采购备注。</summary>
        public string Remark { get; set; }
        /// <summary>特殊及包装要求。</summary>
        public string PackagingRequirements { get; set; }
        /// <summary>Excel 原采购单号，系统 OrderNo 另行生成。</summary>
        public string BoNo { get; set; }
        /// <summary>客户 PO 号快照。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>供应商回复交期。</summary>
        public DateTime? ReplyDate { get; set; }
        /// <summary>业务员名称快照，不作为审计身份。</summary>
        public string SalesPerson { get; set; }
        /// <summary>跟单员名称快照，不作为审计身份。</summary>
        public string PurchasePerson { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string InletHoseType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string AeratorType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string PalletType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string BoxType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string LabelType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string InstructionManualType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string CartonType { get; set; }
        /// <summary>包装或配置要求快照。</summary>
        public string LogoType { get; set; }
        /// <summary>价格倍率记录，不调整行价格或计算整单应付。</summary>
        public decimal? DiscountRate { get; set; }
        /// <summary>手工金额调整记录，不参与导入金额计算。</summary>
        public decimal? DiscountAmount { get; set; }
        /// <summary>价格调整备注。</summary>
        public string DiscountRemark { get; set; }
    }
}


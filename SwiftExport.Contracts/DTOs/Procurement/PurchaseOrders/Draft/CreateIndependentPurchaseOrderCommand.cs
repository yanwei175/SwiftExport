using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>创建有客户归属和实际供应商的独立采购；不接受需求来源或实际收货仓。</summary>
    public sealed class CreateIndependentPurchaseOrderCommand
    {
        public Guid CommandId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid SupplierId { get; set; }
        public DateTime OrderDate { get; set; }
        public string Remark { get; set; }
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

        public List<CreateIndependentPurchaseOrderLineCommand> Lines { get; set; } = new List<CreateIndependentPurchaseOrderLineCommand>();
    }
}

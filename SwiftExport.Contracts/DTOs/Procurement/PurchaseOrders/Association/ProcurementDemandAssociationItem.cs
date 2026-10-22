using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>一条明确的采购行到物料需求分配意图；预览与提交使用同一身份和数量。</summary>
    public sealed class ProcurementDemandAssociationItem
    {
        public Guid CommandItemId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedLineVersion { get; set; }
        public Guid NetDemandId { get; set; }
        public int ExpectedDemandVersion { get; set; }
        /// <summary>预览时读取的 GrossDemand.Version，不能提交原来源单据的版本。</summary>
        public int ExpectedSourceDemandVersion { get; set; }
        /// <summary>本次关联数量，必须为正数且最多四位小数；按采购行和物料需求分别汇总检查可分配量。</summary>
        public decimal Quantity { get; set; }
        /// <summary>非标行必须由用户明确确认匹配，不按描述自动认定身份。</summary>
        public bool AcceptNonStandardMatch { get; set; }
    }
}

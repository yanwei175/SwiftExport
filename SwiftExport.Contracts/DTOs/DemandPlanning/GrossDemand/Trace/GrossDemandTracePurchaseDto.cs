using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace
{
    /// <summary>物料需求到采购行的 Link 证据；货好量属于整条采购行。</summary>
    public class GrossDemandTracePurchaseDto
    {
        public Guid NetDemandId { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid PurchaseLineId { get; set; }
        /// <summary>采购头行仍可定位时允许导航。</summary>
        public bool HasPurchaseTarget { get; set; }
        public string PurchaseOrderNo { get; set; }
        public int PurchaseLineNumber { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>Link 原始分配量，历史取消后仍可追溯。</summary>
        public decimal AllocatedQty { get; set; }
        /// <summary>Link 已释放量。</summary>
        public decimal ReleasedQty { get; set; }
        public decimal LinkedQty { get; set; }
        public decimal PurchaseLineQty { get; set; }
        public decimal? PurchaseLineReadyQty { get; set; }
        /// <summary>整条采购行的有效已过账采购入库量，不按 Link 分摊。</summary>
        public decimal PurchaseLineReceivedQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public bool IsSharedPurchaseLine { get; set; }
        public string ReviewReason { get; set; }
    }
}

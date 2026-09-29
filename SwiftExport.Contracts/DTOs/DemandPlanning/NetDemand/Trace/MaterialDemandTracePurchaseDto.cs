using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace
{
    /// <summary>物料需求到采购行的 Link 及整行履约事实。</summary>
    public class MaterialDemandTracePurchaseDto
    {
        /// <summary>Link 身份。</summary>
        public Guid LinkId { get; set; }
        /// <summary>采购头身份。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>采购行身份。</summary>
        public Guid PurchaseLineId { get; set; }
        /// <summary>采购目标可导航。</summary>
        public bool HasPurchaseTarget { get; set; }
        /// <summary>采购单号。</summary>
        public string PurchaseOrderNo { get; set; }
        /// <summary>采购行号。</summary>
        public int PurchaseLineNumber { get; set; }
        /// <summary>实际采购供应商。</summary>
        public string ActualSupplierName { get; set; }
        /// <summary>采购头确认状态。</summary>
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        /// <summary>采购头生命周期。</summary>
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>Link 是否当前有效。</summary>
        public bool IsActiveLink { get; set; }
        /// <summary>原分配量。</summary>
        public decimal AllocatedQty { get; set; }
        /// <summary>已释放量。</summary>
        public decimal ReleasedQty { get; set; }
        /// <summary>Link 净贡献。</summary>
        public decimal LinkedQty { get; set; }
        /// <summary>整条采购行数量。</summary>
        public decimal PurchaseLineQty { get; set; }
        /// <summary>采购行单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>最新整行货好量。</summary>
        public decimal? PurchaseLineReadyQty { get; set; }
        /// <summary>最新预计货好日期。</summary>
        public DateTime? ExpectedReadyDate { get; set; }
        /// <summary>整行正式净收货量。</summary>
        public decimal PurchaseLineReceivedQty { get; set; }
        /// <summary>分给本 Link 的当前收货覆盖；历史或异常关系为空。</summary>
        public decimal? CurrentReceiptCoverageQty { get; set; }
        /// <summary>整行未分给当前有效需求的实收投影。</summary>
        public decimal? ReceiptOutsideCurrentCoverageQty { get; set; }
        /// <summary>整行历史 Link 净贡献；不表示历史收货归属。</summary>
        public decimal HistoricalLinkedQty { get; set; }
        /// <summary>采购行也关联其他需求。</summary>
        public bool IsSharedPurchaseLine { get; set; }
        /// <summary>关系与数量可信度提示。</summary>
        public string ReviewReason { get; set; }
    }
}

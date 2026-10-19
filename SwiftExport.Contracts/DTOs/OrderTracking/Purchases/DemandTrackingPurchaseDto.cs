using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Purchases
{
    /// <summary>一行一条采购关联；采购整行数量不能按关联重复汇总。</summary>
    public class DemandTrackingPurchaseDto
    {
        /// <summary>采购关联稳定身份。</summary>
        public Guid LinkId { get; set; }
        /// <summary>关联物料身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>物料修订号。</summary>
        public int DecompositionRevision { get; set; }
        /// <summary>采购单身份。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>采购行身份。</summary>
        public Guid PurchaseLineId { get; set; }
        /// <summary>当前采购头行是否可定位。</summary>
        public bool HasPurchaseTarget { get; set; }
        /// <summary>采购单号。</summary>
        public string PurchaseOrderNo { get; set; }
        /// <summary>采购行号。</summary>
        public int PurchaseLineNumber { get; set; }
        /// <summary>当前实际采购供应商显示名称。</summary>
        public string SupplierName { get; set; }
        /// <summary>采购行产品编码。</summary>
        public string Sku { get; set; }
        /// <summary>采购行单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>采购确认状态。</summary>
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        /// <summary>采购生命周期。</summary>
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>原始关联分配量。</summary>
        public decimal AllocatedQty { get; set; }
        /// <summary>已释放量。</summary>
        public decimal ReleasedQty { get; set; }
        /// <summary>关联净贡献，AllocatedQty减ReleasedQty。</summary>
        public decimal LinkedQty { get; set; }
        /// <summary>当前关联是否活动且未删除，不代替证据校验。</summary>
        public bool IsActiveLink { get; set; }
        /// <summary>采购整行数量，目标缺失为未知。</summary>
        public decimal? PurchaseLineQty { get; set; }
        /// <summary>是否被其他需求或物料关联共享。</summary>
        public bool IsSharedPurchaseLine { get; set; }
        /// <summary>目标、单位、客户、来源或历史关联说明。</summary>
        public string ReviewReason { get; set; }
    }
}

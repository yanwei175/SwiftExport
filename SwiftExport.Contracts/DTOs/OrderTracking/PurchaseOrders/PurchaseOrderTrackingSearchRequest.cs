using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders
{
    /// <summary>采购单视角跟踪分页条件；独立采购无需需求关联，进度筛选要求对应维度权限。</summary>
    public sealed class PurchaseOrderTrackingSearchRequest : PagedRequestBase
    {
        /// <summary>采购头客户代码快照多选，空集合不限。</summary>
        public List<string> CustomerCodes { get; set; } = new List<string>();
        /// <summary>实际供应商身份多选，空集合不限。</summary>
        public List<Guid> SupplierIds { get; set; } = new List<Guid>();
        /// <summary>下单日期起日，包含当天。</summary>
        public DateTime? OrderDateFrom { get; set; }
        /// <summary>下单日期止日，包含当天。</summary>
        public DateTime? OrderDateTo { get; set; }
        /// <summary>确认状态集合内取并集，空集合不限。</summary>
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        /// <summary>单据生命周期集合内取并集，空集合不限。</summary>
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        /// <summary>供应商货好状态，使用时必须有反馈查看权限。</summary>
        public List<PurchaseReadyStatusEnum> ReadyStatuses { get; set; } = new List<PurchaseReadyStatusEnum>();
        /// <summary>正式收货状态，使用时必须有入库查看权限。</summary>
        public List<PurchaseReceiptStatusEnum> ReceiptStatuses { get; set; } = new List<PurchaseReceiptStatusEnum>();
        /// <summary>未货好交期风险视图，非全部条件要求反馈查看权限。</summary>
        public PurchaseDueViewEnum DueView { get; set; }
        /// <summary>保存的采购行计划交期起日。</summary>
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        /// <summary>保存的采购行计划交期止日。</summary>
        public DateTime? PlannedDeliveryDateTo { get; set; }
        /// <summary>供应商反馈预计完成日期起日，要求反馈查看权限。</summary>
        public DateTime? ExpectedCompletionDateFrom { get; set; }
        /// <summary>供应商反馈预计完成日期止日，要求反馈查看权限。</summary>
        public DateTime? ExpectedCompletionDateTo { get; set; }
    }
}

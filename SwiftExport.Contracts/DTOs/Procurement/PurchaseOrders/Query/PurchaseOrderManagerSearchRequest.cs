using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购管理页的新链专用分页筛选；空状态集合表示不限制对应状态。</summary>
    public sealed class PurchaseOrderManagerSearchRequest : PagedRequestBase
    {
        /// <summary>按采购头保存的客户编码精确筛选，不联查客户主数据。</summary>
        public string CustomerCode { get; set; }
        public Guid? SupplierId { get; set; }
        /// <summary>下单日期起止均按自然日包含边界；空值表示不限制。</summary>
        public DateTime? OrderDateFrom { get; set; }
        public DateTime? OrderDateTo { get; set; }
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        /// <summary>整单货好状态，多选取并集；空集合不限制。</summary>
        public List<PurchaseReadyStatusEnum> ReadyStatuses { get; set; } = new List<PurchaseReadyStatusEnum>();
        /// <summary>整单正式收货状态，多选取并集；空集合不限制。</summary>
        public List<PurchaseReceiptStatusEnum> ReceiptStatuses { get; set; } = new List<PurchaseReceiptStatusEnum>();
        /// <summary>单选交期风险视图，与普通条件取交集。</summary>
        public PurchaseDueViewEnum DueView { get; set; }
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
        public DateTime? ExpectedCompletionDateFrom { get; set; }
        public DateTime? ExpectedCompletionDateTo { get; set; }
    }
}

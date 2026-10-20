using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商反馈录入上下文；提供当前订单版本、登记资格及各行追加式反馈历史。</summary>
    public sealed class PurchaseOrderProgressDetailDto
    {
        public Guid PurchaseOrderId { get; set; }
        /// <summary>当前采购头版本，提交反馈时用于并发校验。</summary>
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string SupplierName { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>订单当前状态是否允许登记；不替代调用者权限及提交时服务端复验。</summary>
        public bool CanReportProgress { get; set; }
        /// <summary>订单状态导致的不可登记原因；调用者权限由授权边界另行处理。</summary>
        public string ReadOnlyReason { get; set; }
        public List<PurchaseOrderProgressLineDto> Lines { get; set; } = new List<PurchaseOrderProgressLineDto>();
    }
}

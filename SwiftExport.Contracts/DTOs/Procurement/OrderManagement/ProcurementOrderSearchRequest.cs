using System;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>仅用于新采购管理的分页筛选，不接受供应商跟进条件。</summary>
    public sealed class ProcurementOrderSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
        public Guid? SupplierId { get; set; }
        public ConfirmationStatusEnum? ConfirmationStatus { get; set; }
    }
}

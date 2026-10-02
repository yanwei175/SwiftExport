using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>采购导入检查快照，正式创建必须重新校验。</summary>
    public sealed class CheckPurchaseOrderImportResponse
    {
        /// <summary>唯一有效客户，失败时为空。</summary>
        public PurchaseOrderImportCustomerDto Customer { get; set; }
        /// <summary>有效实际供应商，失败时为空。</summary>
        public PurchaseOrderImportSupplierDto Supplier { get; set; }
        /// <summary>本次快照没有未解决的阻断问题。</summary>
        public bool CanSave { get; set; }
        /// <summary>整单问题。</summary>
        public List<PurchaseOrderImportIssueDto> Issues { get; set; } = new List<PurchaseOrderImportIssueDto>();
        /// <summary>逐行检查结果。</summary>
        public List<PurchaseOrderImportLineCheckDto> Lines { get; set; } = new List<PurchaseOrderImportLineCheckDto>();
    }
}


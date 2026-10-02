using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>采购导入只读检查；供应商通过现有选择接口明确选择，不按名称猜测身份。</summary>
    public sealed class CheckPurchaseOrderImportRequest
    {
        /// <summary>Excel 客户代码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>用户选择的实际供应商。</summary>
        public Guid SupplierId { get; set; }
        /// <summary>待检查单头。</summary>
        public PurchaseOrderImportHeaderInput Header { get; set; } = new PurchaseOrderImportHeaderInput();
        /// <summary>待检查行，最多五百条。</summary>
        public List<PurchaseOrderImportLineInput> Lines { get; set; } = new List<PurchaseOrderImportLineInput>();
    }
}


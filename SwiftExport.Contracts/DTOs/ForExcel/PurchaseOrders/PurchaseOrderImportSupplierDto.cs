using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>用户选择的实际采购供应商资格。</summary>
    public sealed class PurchaseOrderImportSupplierDto
    {
        /// <summary>供应商身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>供应商代码。</summary>
        public string SupplierCode { get; set; }
        /// <summary>供应商名称。</summary>
        public string SupplierName { get; set; }
        /// <summary>是否启用。</summary>
        public bool IsActive { get; set; }
    }
}


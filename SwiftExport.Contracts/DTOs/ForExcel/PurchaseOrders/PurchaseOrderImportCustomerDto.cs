using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>采购导入客户定位结果，不复用销售用例契约。</summary>
    public sealed class PurchaseOrderImportCustomerDto
    {
        /// <summary>客户身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>客户代码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>客户名称。</summary>
        public string CustomerName { get; set; }
        /// <summary>当前是否启用。</summary>
        public bool IsActive { get; set; }
    }
}


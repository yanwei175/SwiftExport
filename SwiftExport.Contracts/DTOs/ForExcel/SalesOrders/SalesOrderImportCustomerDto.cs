using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>通过客户代码查询的导入客户身份。</summary>
    public class SalesOrderImportCustomerDto
    {
        /// <summary>有效客户身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>数据库客户代码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>客户名称。</summary>
        public string CustomerName { get; set; }
        /// <summary>是否可用于新导入。</summary>
        public bool IsActive { get; set; }
    }
}

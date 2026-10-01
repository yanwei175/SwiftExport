using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>只读检查结果，不是保存授权凭证；正式保存仍需事务内重验。</summary>
    public class CheckSalesOrderImportResponse
    {
        /// <summary>唯一有效客户，定位失败时为空。</summary>
        public SalesOrderImportCustomerDto Customer { get; set; }
        /// <summary>无阻断错误且全部确认提示已确认；仅代表本次检查快照。</summary>
        public bool CanSave { get; set; }
        /// <summary>单头及整单问题。</summary>
        public List<SalesOrderImportIssueDto> Issues { get; set; } = new List<SalesOrderImportIssueDto>();
        /// <summary>与请求逐行对应的结果。</summary>
        public List<SalesOrderImportLineCheckDto> Lines { get; set; } = new List<SalesOrderImportLineCheckDto>();
        /// <summary>已有订单候选，仅提示，不覆盖已有单据。</summary>
        public List<SalesOrderImportExistingOrderDto> ExistingOrders { get; set; } = new List<SalesOrderImportExistingOrderDto>();
    }
}

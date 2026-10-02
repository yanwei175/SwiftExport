using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>字段错误或需用户确认的差异。</summary>
    public sealed class PurchaseOrderImportIssueDto
    {
        /// <summary>稳定问题代码。</summary>
        public string Code { get; set; }
        /// <summary>问题字段。</summary>
        public string Field { get; set; }
        /// <summary>中文说明。</summary>
        public string Message { get; set; }
        /// <summary>是否可由用户确认消除阻断。</summary>
        public bool RequiresConfirmation { get; set; }
        /// <summary>本次请求是否确认此提示。</summary>
        public bool Acknowledged { get; set; }
    }
}


using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>检查结果中的稳定问题代码与输入字段位置。</summary>
    public class SalesOrderImportIssueDto
    {
        /// <summary>客户端分支使用的稳定问题代码。</summary>
        public string Code { get; set; }
        /// <summary>输入字段名，行问题由所属结果定位。</summary>
        public string Field { get; set; }
        /// <summary>可显示的中文问题说明。</summary>
        public string Message { get; set; }
        /// <summary>为 true 时需要用户确认；为 false 时是阻断错误。</summary>
        public bool RequiresConfirmation { get; set; }
        /// <summary>本次请求是否已明确确认该提示。</summary>
        public bool Acknowledged { get; set; }
    }
}

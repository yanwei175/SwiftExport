using System;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>后端检查的不可变SalesOrderImportCheckIssue快照，防止共享 DTO 后续修改页面结果。</summary>
    public sealed class SalesOrderImportCheckIssue
    {
        /// <summary>复制已接纳的检查反馈；不执行网络或业务字段赋值。</summary>
        public SalesOrderImportCheckIssue(SalesOrderImportIssueDto value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Code = value.Code ?? "";
            Field = value.Field ?? "";
            Message = value.Message ?? "";
            RequiresConfirmation = value.RequiresConfirmation;
            Acknowledged = value.Acknowledged;
        }
        public string Code { get; }
        public string Field { get; }
        public string Message { get; }
        public bool RequiresConfirmation { get; }
        public bool Acknowledged { get; }
    }
}

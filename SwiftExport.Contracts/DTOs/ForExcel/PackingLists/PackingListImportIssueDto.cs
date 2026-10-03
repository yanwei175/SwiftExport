using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>PackingList 导入结果；只传递业务值，不持有界面状态。</summary>
    public sealed class PackingListImportIssueDto
    {
        public string Code { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
        public bool RequiresConfirmation { get; set; }
        public bool Acknowledged { get; set; }
    }
}

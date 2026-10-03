using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>PackingList 导入结果；只传递业务值，不持有界面状态。</summary>
    public sealed class CheckPackingListImportResponse
    {
        public bool CanSave { get; set; }
        public List<PackingListImportIssueDto> Issues { get; set; } = new List<PackingListImportIssueDto>();
        public List<PackingListImportLineCheckDto> Lines { get; set; } = new List<PackingListImportLineCheckDto>();
    }
}

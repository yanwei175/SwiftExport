using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>PackingList 导入请求；只传递业务值，不持有界面状态。</summary>
    public sealed class CheckPackingListImportRequest
    {
        public PackingListDraftHeaderInput Header { get; set; } = new PackingListDraftHeaderInput();
        public List<PackingListDraftLineInput> Lines { get; set; } = new List<PackingListDraftLineInput>();
    }
}

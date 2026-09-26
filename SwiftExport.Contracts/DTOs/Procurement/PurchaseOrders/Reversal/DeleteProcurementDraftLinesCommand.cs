using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>立即删除选定草稿行；释放数量由服务端计算，不接收商务修改。</summary>
    public sealed class DeleteProcurementDraftLinesCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
        public List<ProcurementLineVersionDto> Lines { get; set; } = new List<ProcurementLineVersionDto>();
    }
}

using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>删除从未确认且没有下游历史的整张活动草稿；头行逻辑删除和来源占用释放由服务端原子执行。</summary>
    public sealed class DeleteProcurementDraftCommand
    {
        /// <summary>本次删除命令身份；未知结果重试时必须保留。</summary>
        public Guid CommandId { get; set; }
        /// <summary>用户读取的采购头版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>删除原因，保留在业务审计中。</summary>
        public string Reason { get; set; }
        /// <summary>全部当前有效采购行及其版本，防止遗漏并发追加或修改。</summary>
        public List<ProcurementLineVersionDto> Lines { get; set; } = new List<ProcurementLineVersionDto>();
    }
}

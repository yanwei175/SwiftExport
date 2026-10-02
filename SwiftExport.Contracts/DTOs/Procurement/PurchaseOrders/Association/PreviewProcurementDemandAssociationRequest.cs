using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>只读分配预览输入；不占用需求或采购剩余量。</summary>
    public sealed class PreviewProcurementDemandAssociationRequest
    {
        public int ExpectedVersion { get; set; }
        public List<ProcurementDemandAssociationItem> Items { get; set; } = new List<ProcurementDemandAssociationItem>();
    }
}

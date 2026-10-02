using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购行当前有效贡献的只读身份和来源，用于人工核对多次关联。</summary>
    public sealed class ProcurementDemandAssociationLinkDto
    {
        public Guid LinkId { get; set; }
        public int Version { get; set; }
        public Guid NetDemandId { get; set; }
        public string SourceNo { get; set; }
        public string DemandSku { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
    }
}

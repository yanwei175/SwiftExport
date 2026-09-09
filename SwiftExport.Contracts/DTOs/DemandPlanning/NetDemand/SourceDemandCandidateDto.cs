using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料领域根据来源需求与物料历史计算出的建立候选。</summary>
    public class SourceDemandCandidateDto
    {
        public Guid GrossDemandId { get; set; }
        public int SourceDemandVersion { get; set; }
        public int? SourceVersion { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public int SourceLineNumber { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public int LatestDecompositionRevision { get; set; }
        public int ActiveMaterialDemandCount { get; set; }
        public string EstablishmentState { get; set; }
        public bool CanEstablish { get; set; }
        public string BlockerCode { get; set; }
        public string BlockerMessage { get; set; }
    }
}

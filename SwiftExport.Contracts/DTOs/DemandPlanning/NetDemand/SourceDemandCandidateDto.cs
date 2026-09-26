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
        /// <summary>来源需求建立时保存的关联 SKU 快照。</summary>
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        /// <summary>来源需求建立时保存的产品图片路径快照。</summary>
        public string ImagePath { get; set; }
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
    }
}

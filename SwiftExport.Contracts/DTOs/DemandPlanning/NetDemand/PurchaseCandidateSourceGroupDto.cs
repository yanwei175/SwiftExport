using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>采购选择器中一张仍包含待采购物料的来源单。</summary>
    public class PurchaseCandidateSourceGroupDto
    {
        /// <summary>来源业务类型，与来源 Uid 共同构成稳定查询身份。</summary>
        public string SourceType { get; set; }

        /// <summary>来源业务单据 Uid。</summary>
        public Guid SourceUid { get; set; }

        /// <summary>来源业务单号快照。</summary>
        public string SourceNo { get; set; }

        /// <summary>当前仍有剩余量的采购物料数量。</summary>
        public int CandidateCount { get; set; }
    }
}

using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>表示仍包含待建立物料需求行的一张来源单。</summary>
    public class SourceDemandCandidateGroupDto
    {
        /// <summary>来源单据类型，例如 SO、MD 或 CR。</summary>
        public string SourceType { get; set; }

        /// <summary>来源业务单据的稳定标识，与来源类型共同确定查询边界。</summary>
        public Guid SourceUid { get; set; }

        /// <summary>供用户识别的来源单号快照。</summary>
        public string SourceNo { get; set; }

        /// <summary>该来源单当前尚可建立物料需求的来源行数量。</summary>
        public int CandidateCount { get; set; }
    }
}

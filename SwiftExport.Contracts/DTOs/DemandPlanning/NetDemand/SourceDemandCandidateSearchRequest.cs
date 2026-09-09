using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求侧查询可建立来源需求的分页条件。</summary>
    public class SourceDemandCandidateSearchRequest : PagedRequestBase
    {
        /// <summary>是否同时返回已建立或待处置的来源需求。</summary>
        public bool IncludeUnavailable { get; set; }
    }
}

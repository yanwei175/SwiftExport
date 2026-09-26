using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 净需求来源导航中的一张来源单及其匹配净需求数量。
    /// </summary>
    public class NetDemandSourceGroupDto
    {
        /// <summary>
        /// 来源业务单据的权威标识。
        /// </summary>
        public Guid SourceUid { get; set; }

        /// <summary>
        /// 来源类型快照，例如 SO、MD 或 CR。
        /// </summary>
        public string SourceType { get; set; }

        /// <summary>
        /// 供用户识别和检索的来源单号快照。
        /// </summary>
        public string SourceNo { get; set; }

        /// <summary>
        /// 当前独立筛选条件下的活动净需求数量。
        /// </summary>
        public int DemandCount { get; set; }
    }
}

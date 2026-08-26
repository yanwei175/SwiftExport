using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 净需求来源导航的独立筛选条件，不受工作台顶部主筛选影响。
    /// </summary>
    public class NetDemandSourceGroupSearchRequest
    {
        /// <summary>
        /// 允许显示的来源类型；空集合表示全部来源类型。
        /// </summary>
        public List<string> SourceTypes { get; set; } = new List<string>();

        /// <summary>
        /// 允许显示的净需求计划状态；空集合表示全部计划状态。
        /// </summary>
        public List<NetDemandPlanStatusEnum> PlanStatuses { get; set; } = new List<NetDemandPlanStatusEnum>();
    }

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

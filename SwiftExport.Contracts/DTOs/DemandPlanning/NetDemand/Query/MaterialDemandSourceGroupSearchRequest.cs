using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求工作台来源导航筛选，仅按来源事实筛选。</summary>
    public class MaterialDemandSourceGroupSearchRequest
    {
        /// <summary>允许显示的来源类型；空集合表示全部。</summary>
        public List<string> SourceTypes { get; set; } = new List<string>();
    }
}

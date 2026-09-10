using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>采购选择器完整候选查询；来源为空表示加载全部待采购物料。</summary>
    public class PurchaseCandidateQuery
    {
        /// <summary>来源业务类型；按单查询时与 SourceUid 同时提供。</summary>
        public string SourceType { get; set; }

        /// <summary>来源业务单据 Uid；为空时查询全部来源。</summary>
        public Guid? SourceUid { get; set; }
    }
}

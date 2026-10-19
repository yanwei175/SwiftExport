using System.ComponentModel;

namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>采购行有效需求贡献的查询状态；不落库，不替代确认状态或单据生命周期。</summary>
    public enum PurchaseDemandAssociationStatusEnum
    {
        /// <summary>没有有效需求贡献，全部采购数量尚未关联。</summary>
        [Description("未关联")] Unassociated = 0,
        /// <summary>部分采购数量已由有效需求贡献承接。</summary>
        [Description("部分关联")] Partial = 1,
        /// <summary>全部采购数量已由有效需求贡献承接。</summary>
        [Description("已关联")] Associated = 2,
        /// <summary>采购已作废，仅保留历史贡献，不再提供当前关联数量。</summary>
        [Description("不适用")] NotApplicable = 8,
        /// <summary>引用身份、依据或数量异常，不能得出可信关联结论。</summary>
        [Description("需核实")] NeedsReview = 9
    }
}

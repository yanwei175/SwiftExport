using System.ComponentModel;

namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>采购单有效行的累计货好查询状态；只由查询投影计算，不落库。</summary>
    public enum PurchaseReadyStatusEnum
    {
        [Description("无明细")] NoLines = 0,
        [Description("未反馈")] NotReported = 1,
        [Description("数量未知")] QuantityUnknown = 2,
        [Description("未货好")] NotReady = 3,
        [Description("部分货好")] PartiallyReady = 4,
        [Description("全部货好")] FullyReady = 5,
        [Description("需核实")] NeedsReview = 9
    }

    /// <summary>采购单有效行的正式净收货查询状态；不代表库存余额。</summary>
    public enum PurchaseReceiptStatusEnum
    {
        [Description("无明细")] NoLines = 0,
        [Description("未收")] NotReceived = 1,
        [Description("部分收")] PartiallyReceived = 2,
        [Description("已收")] FullyReceived = 3,
        [Description("需核实")] NeedsReview = 9
    }

    /// <summary>采购管理页按尚未确认货好的采购行筛选交期风险。</summary>
    public enum PurchaseDueViewEnum
    {
        [Description("全部")] All = 0,
        [Description("逾期未货好")] OverdueNotReady = 1,
        [Description("临期未货好")] DueSoonNotReady = 2
    }
}

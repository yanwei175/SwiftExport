namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 采购单据自身的生命周期；草稿由 Active + 待确认共同表达。
    /// </summary>
    public enum PurchaseOrderLifecycleStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已关闭")]
        Closed = 8,
        [System.ComponentModel.Description("已作废")]
        Voided = 9
    }

    /// <summary>供应商对采购行的生产安排状态。</summary>
    public enum SupplierArrangementStatusEnum
    {
        [System.ComponentModel.Description("未知")]
        Unknown = 0,
        [System.ComponentModel.Description("未安排")]
        NotArranged = 1,
        [System.ComponentModel.Description("已安排")]
        Arranged = 2,
        [System.ComponentModel.Description("不适用")]
        NotApplicable = 8
    }

    /// <summary>采购工作台命令结果；用于区分首次成功与幂等重试。</summary>
    public enum ProcurementCommandOutcomeEnum
    {
        [System.ComponentModel.Description("成功")]
        Succeeded = 0,
        [System.ComponentModel.Description("已应用")]
        AlreadyApplied = 1
    }
}

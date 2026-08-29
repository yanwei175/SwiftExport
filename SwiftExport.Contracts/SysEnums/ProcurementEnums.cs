namespace SwiftExport.Contracts.SysEnums
{
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
        Succeeded = 0,
        AlreadyApplied = 1
    }
}

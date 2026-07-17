namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>供应商对采购行的生产安排状态。</summary>
    public enum SupplierArrangementStatusEnum
    {
        Unknown = 0,
        NotArranged = 1,
        Arranged = 2,
        NotApplicable = 8
    }

    /// <summary>采购工作台命令结果；用于区分首次成功与幂等重试。</summary>
    public enum ProcurementCommandOutcomeEnum
    {
        Succeeded = 0,
        AlreadyApplied = 1
    }
}

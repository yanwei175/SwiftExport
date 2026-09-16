namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 毛需求自身的生命周期，不承载拆解或执行进度。
    /// </summary>
    public enum GrossDemandLifecycleStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已关闭")]
        Closed = 8,
        [System.ComponentModel.Description("已作废")]
        Voided = 9
    }

    /// <summary>
    /// 净需求拆解版本的生命周期。
    /// </summary>
    public enum NetDemandLifecycleStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已回滚")]
        RolledBack = 8,
        [System.ComponentModel.Description("已作废")]
        Voided = 9
    }

    /// <summary>
    /// 净需求的交付目标解析方式。
    /// </summary>
    public enum DemandDeliveryModeEnum
    {
        [System.ComponentModel.Description("标准交付")]
        Standard = 0,
        [System.ComponentModel.Description("父级执行供应商")]
        ParentExecutionSupplier = 1
    }

    /// <summary>
    /// 净需求到执行单的追溯链接状态。
    /// </summary>
    public enum DemandExecutionLinkStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已取消")]
        Cancelled = 8,
        [System.ComponentModel.Description("已冲销")]
        Reversed = 9
    }

    /// <summary>
    /// 由执行链接和正式单据查询计算的进度，不持久化到毛需求。
    /// </summary>
    public enum DemandExecutionProgressEnum
    {
        [System.ComponentModel.Description("未开始")]
        NotStarted = 0,
        [System.ComponentModel.Description("部分分配")]
        PartiallyAllocated = 1,
        [System.ComponentModel.Description("全部分配")]
        FullyAllocated = 2,
        [System.ComponentModel.Description("部分完成")]
        PartiallyCompleted = 3,
        [System.ComponentModel.Description("已完成")]
        Completed = 4
    }

    /// <summary>
    /// 净需求的计划执行方式。
    /// </summary>
    public enum DemandExecutionTypeEnum
    {
        [System.ComponentModel.Description("采购")]
        Purchase = 0,
        [System.ComponentModel.Description("生产")]
        Production = 1,
        [System.ComponentModel.Description("外协")]
        Outsourcing = 2
    }

    /// <summary>
    /// 采购承接物料需求命令的逐项结果。
    /// </summary>
    public enum NetDemandCommandOutcomeEnum
    {
        [System.ComponentModel.Description("成功")]
        Succeeded = 0,
        [System.ComponentModel.Description("已应用")]
        AlreadyApplied = 1,
        [System.ComponentModel.Description("失败")]
        Failed = 2
    }

    /// <summary>
    /// 毛需求批量命令的逐项结果。
    /// </summary>
    public enum GrossDemandCommandOutcomeEnum
    {
        [System.ComponentModel.Description("成功")]
        Succeeded = 0,
        [System.ComponentModel.Description("已应用")]
        AlreadyApplied = 1,
        [System.ComponentModel.Description("失败")]
        Failed = 2
    }
}

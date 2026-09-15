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
    /// 毛需求当前是否存在活动拆解版本。
    /// </summary>
    public enum GrossDemandDecompositionStatusEnum
    {
        [System.ComponentModel.Description("待拆解")]
        Pending = 0,
        [System.ComponentModel.Description("已拆解")]
        Decomposed = 1
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
    /// 净需求计划分配的生命周期。计划调整通过新增替代记录保留历史。
    /// </summary>
    public enum NetDemandAllocationLifecycleStatusEnum
    {
        [System.ComponentModel.Description("活动")]
        Active = 0,
        [System.ComponentModel.Description("已替代")]
        Superseded = 8,
        [System.ComponentModel.Description("已取消")]
        Cancelled = 9
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
    /// 计划供应商或执行方式的分配来源。
    /// </summary>
    public enum SupplierAssignmentSourceEnum
    {
        [System.ComponentModel.Description("产品默认")]
        ProductDefault = 0,
        [System.ComponentModel.Description("未分配")]
        Unassigned = 1,
        [System.ComponentModel.Description("手工分配")]
        Manual = 2,
        [System.ComponentModel.Description("重新分配")]
        Reassigned = 3,
        [System.ComponentModel.Description("已应用产品默认")]
        AppliedProductDefault = 4,
        [System.ComponentModel.Description("拆分")]
        Split = 5
    }

    /// <summary>
    /// 净需求工作台查询计算状态，不持久化到净需求或计划分配。
    /// </summary>
    public enum NetDemandPlanStatusEnum
    {
        [System.ComponentModel.Description("供应商未分配")]
        UnassignedSupplier = 0,
        [System.ComponentModel.Description("交付目标待定")]
        DeliveryTargetPending = 1,
        [System.ComponentModel.Description("可转采购")]
        ReadyForPurchase = 2,
        [System.ComponentModel.Description("部分分配")]
        PartiallyAllocated = 3,
        [System.ComponentModel.Description("全部分配")]
        FullyAllocated = 4,
        [System.ComponentModel.Description("下游草稿")]
        DownstreamDraft = 5,
        [System.ComponentModel.Description("下游已确认")]
        DownstreamConfirmed = 6,
        [System.ComponentModel.Description("数据异常")]
        DataCorrupted = 8,
        [System.ComponentModel.Description("执行工作台待处理")]
        ExecutionWorkspacePending = 9
    }

    /// <summary>
    /// 净需求业务命令的逐项结果。
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

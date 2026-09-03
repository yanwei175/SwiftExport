namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 毛需求自身的生命周期，不承载拆解或执行进度。
    /// </summary>
    public enum GrossDemandLifecycleStatusEnum
    {
        Active = 0,
        Closed = 8,
        Voided = 9
    }

    /// <summary>
    /// 毛需求当前是否存在活动拆解版本。
    /// </summary>
    public enum GrossDemandDecompositionStatusEnum
    {
        Pending = 0,
        Decomposed = 1
    }

    /// <summary>
    /// 净需求拆解版本的生命周期。
    /// </summary>
    public enum NetDemandLifecycleStatusEnum
    {
        Active = 0,
        RolledBack = 8,
        Voided = 9
    }

    /// <summary>
    /// 净需求的交付目标解析方式。
    /// </summary>
    public enum DemandDeliveryModeEnum
    {
        Standard = 0,
        ParentExecutionSupplier = 1
    }

    /// <summary>
    /// 净需求到执行单的追溯链接状态。
    /// </summary>
    public enum DemandExecutionLinkStatusEnum
    {
        Active = 0,
        Cancelled = 8,
        Reversed = 9
    }

    /// <summary>
    /// 由执行链接和正式单据查询计算的进度，不持久化到毛需求。
    /// </summary>
    public enum DemandExecutionProgressEnum
    {
        NotStarted = 0,
        PartiallyAllocated = 1,
        FullyAllocated = 2,
        PartiallyCompleted = 3,
        Completed = 4
    }

    /// <summary>
    /// 净需求计划分配的生命周期。计划调整通过新增替代记录保留历史。
    /// </summary>
    public enum NetDemandAllocationLifecycleStatusEnum
    {
        Active = 0,
        Superseded = 8,
        Cancelled = 9
    }

    /// <summary>
    /// 净需求的计划执行方式。
    /// </summary>
    public enum DemandExecutionTypeEnum
    {
        Purchase = 0,
        Production = 1,
        Outsourcing = 2
    }

    /// <summary>
    /// 计划供应商或执行方式的分配来源。
    /// </summary>
    public enum SupplierAssignmentSourceEnum
    {
        ProductDefault = 0,
        Unassigned = 1,
        Manual = 2,
        Reassigned = 3,
        AppliedProductDefault = 4,
        Split = 5
    }

    /// <summary>
    /// 净需求工作台查询计算状态，不持久化到净需求或计划分配。
    /// </summary>
    public enum NetDemandPlanStatusEnum
    {
        UnassignedSupplier = 0,
        DeliveryTargetPending = 1,
        ReadyForPurchase = 2,
        PartiallyAllocated = 3,
        FullyAllocated = 4,
        DownstreamDraft = 5,
        DownstreamConfirmed = 6,
        DataCorrupted = 8,
        ExecutionWorkspacePending = 9
    }

    /// <summary>
    /// 净需求业务命令的逐项结果。
    /// </summary>
    public enum NetDemandCommandOutcomeEnum
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        Failed = 2
    }

    /// <summary>
    /// 毛需求批量命令的逐项结果。
    /// </summary>
    public enum GrossDemandCommandOutcomeEnum
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        Failed = 2
    }
}

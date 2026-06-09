namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>审批状态枚举</summary>
    public enum ApprovalStatusEnum
    {
        待审批 = 0,
        已批准 = 1,
        已驳回 = 9
    }

    /// <summary>销售单状态枚举</summary>
    public enum SalesOrderStatusEnum
    {
        未确认 = 0,
        已确认 = 1,
        部分发货 = 2,
        全部发货 = 3,
        已关闭 = 9
    }
    /// <summary>采购单状态枚举</summary>
    public enum PurchaseOrderStatusEnum
    {
        未确认 = 0,
        已确认 = 1,
        已下单 = 2,
        部分完成 = 3,
        全部完成 = 4,
        已关闭 = 9
    }
    public enum IndependentDemandStatusEnum
    {
        未确认 = 0,
        已确认 = 1,//已分解成净需求
        执行中 = 2,
        部分到货 = 5,
        已到货 = 6,
        部分发货 = 7,
        已完成 = 8
    }
    public enum DependentDemandStatusEnum
    {
        未确认 = 0,
        已确认 = 1,
        已转为正式单据 = 3,
        部分到货 = 5,
        已到货 = 6,
        已完成 = 8,
        已关闭 = 9
    }

}

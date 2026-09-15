namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 销售订单自身的生命周期状态，不包含确认与发货进度。
    /// </summary>
    public enum SalesOrderDocumentStatusEnum
    {
        [System.ComponentModel.Description("开放")]
        Open = 0,
        [System.ComponentModel.Description("已关闭")]
        Closed = 9
    }

    /// <summary>
    /// 销售订单履约进度。该状态由查询汇总计算，不持久化到订单头。
    /// </summary>
    public enum SalesOrderFulfillmentStatusEnum
    {
        [System.ComponentModel.Description("未开始")]
        NotStarted = 0,
        [System.ComponentModel.Description("部分发货")]
        PartiallyShipped = 1,
        [System.ComponentModel.Description("全部发货")]
        FullyShipped = 2
    }
}

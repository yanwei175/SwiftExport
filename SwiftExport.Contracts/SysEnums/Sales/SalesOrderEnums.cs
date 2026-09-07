namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 销售订单自身的生命周期状态，不包含确认与发货进度。
    /// </summary>
    public enum SalesOrderDocumentStatusEnum
    {
        Open = 0,
        Closed = 9
    }

    /// <summary>
    /// 销售订单履约进度。该状态由查询汇总计算，不持久化到订单头。
    /// </summary>
    public enum SalesOrderFulfillmentStatusEnum
    {
        NotStarted = 0,
        PartiallyShipped = 1,
        FullyShipped = 2
    }
}

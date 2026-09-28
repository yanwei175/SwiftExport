namespace SwiftExport.Contracts.DTOs.SalesOrders.Manager
{
    /// <summary>销售管理列表一次选择一个的服务端快捷筛选条件，不作为订单落库状态。</summary>
    public enum SalesOrderManagerQuickView
    {
        All = 0,
        PendingConfirmation = 1,
        NotShipped = 2,
        PartiallyShipped = 3,
        FullyShipped = 4,
        DueSoon = 5,
        Overdue = 6,
        ClosedWithOutstanding = 7,
        NeedsReview = 8
    }
}

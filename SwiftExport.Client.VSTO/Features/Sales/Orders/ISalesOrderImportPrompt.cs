namespace SwiftExport.Client.VSTO.Features.Sales.Orders
{
    /// <summary>页面操作确认边界，测试不需要创建窗口。</summary>
    public interface ISalesOrderImportPrompt
    {
        /// <summary>确认更换客户、替换解析部分、关闭草稿或重复订单仍新建；返回 false 时取消该操作。</summary>
        bool ConfirmDiscard(string message);
    }
}

namespace SwiftExport.Client.VSTO.Features.Sales.Orders
{
    /// <summary>页面数据丢弃确认边界，测试不需要创建窗口。</summary>
    public interface ISalesOrderImportPrompt
    {
        /// <summary>确认更换客户、替换解析部分或关闭内存草稿；返回 false 时保留原内容。</summary>
        bool ConfirmDiscard(string message);
    }
}

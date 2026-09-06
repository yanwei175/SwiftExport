using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    /// <summary>
    /// 确认销售订单命令；CommandId 标识一次业务提交，Version 用于校验销售事实版本。
    /// </summary>
    public class ConfirmSalesOrderCommand
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid CommandId { get; set; }
    }
}

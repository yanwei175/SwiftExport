using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>保存草稿时显式提交的待删除销售订单行身份和版本。</summary>
    public class SalesOrderDeletedLineDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
}

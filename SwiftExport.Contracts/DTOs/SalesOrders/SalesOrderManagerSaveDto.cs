using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 中文注释：订单管理页保存 DTO。
    /// 用于主从表可编辑场景的一次性提交。
    /// </summary>
    public class SalesOrderManagerSaveDto
    {
        /// <summary>
        /// 主表变更集合（订单头）
        /// </summary>
        public BatchRequestDto<SalesOrderHeaderDto> HeaderChanges { get; set; } = new BatchRequestDto<SalesOrderHeaderDto>();

        /// <summary>
        /// 当前编辑订单 ID（用于后端上下文定位当前从表变更对应的订单）
        /// </summary>
        public Guid? CurrentOrderId { get; set; }

        /// <summary>
        /// 当前编辑订单的行变更集合
        /// </summary>
        public BatchRequestDto<SalesOrderLineDto> LineChanges { get; set; } = new BatchRequestDto<SalesOrderLineDto>();
    }
}

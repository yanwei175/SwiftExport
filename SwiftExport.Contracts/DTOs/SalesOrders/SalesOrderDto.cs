using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 销售订单主表 DTO（数据传输对象）。
    /// 用于前后端交互，仅包含 UI 最需要的字段，避免传输冗余数据。
    /// </summary>
    public class SalesOrderDto : BaseDto
    {
        // 中文注释：订单号（唯一标识符，由后端生成，格式如 SO2024010001）
        public string OrderNo { get; set; }

        // 中文注释：客户 ID（用于关联到客户主表）
        public Guid CustomerId { get; set; }

        // 中文注释：客户名称（冗余存储便于列表展示）
        public string CustomerName { get; set; }

        // 中文注释：下单日期
        public DateTime? OrderDate { get; set; }

        // 中文注释：期望交期
        public DateTime? DeliveryDate { get; set; }

        // 中文注释：订单总金额（所有订单行小计的求和）
        public decimal TotalAmount { get; set; }

        // 中文注释：订单状态（待审核、已审核、已发货、已收货、已取消等）
        public int OrderStatus { get; set; }

        // 中文注释：备注/特殊需求描述
        public string Remark { get; set; }

        // 中文注释：订单行数（用于列表页快速展示）
        public int LineCount { get; set; }

        // 中文注释：创建时间（系统记录）
        public DateTime? CreatedTime { get; set; }

        // 中文注释：最后修改时间（系统自动更新）
        public DateTime? ModifiedTime { get; set; }
    }
}

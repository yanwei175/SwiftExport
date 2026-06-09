using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 销售订单详情 DTO（包含订单行列表）。
    /// 用于在编辑/新增订单弹窗中，前后端交互完整的订单数据。
    /// 包含订单主表信息和所有订单行明细。
    /// </summary>
    public class SalesOrderDetailDto
    {
        /// <summary>
        /// 主表（订单头）
        /// </summary>
        public SalesOrderHeaderDto Header { get; set; }

        /// <summary>
        /// 从表（订单行）——用于加载订单
        /// </summary>
        public List<SalesOrderLineDto> Lines { get; set; }

        ///// <summary>
        ///// 扩展字段：专门用于保存时告知后端哪些行需要被物理删除
        ///// 只有在“保存”请求时才会用到这个字段
        ///// </summary>
        //public List<Guid> DeletedLineIds { get; set; } = new List<Guid>();
    }

}

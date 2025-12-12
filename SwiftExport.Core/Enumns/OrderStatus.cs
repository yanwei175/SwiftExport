using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Enumns
{
    public enum OrderStatus
    {
        Pending = 0,      // 待处理
        Approved = 1,     // 已批准
        InProcess = 2,    // 处理中
        Completed = 3,    // 已完成
        Cancelled = 4,    // 已取消
        OnHold = 5        // 暂停
    }

    public enum LineStatus
    {
        Open = 0,         // 未结
        Closed = 1,       // 已结
        Cancelled = 2     // 已取消
    }

    public enum PackingStatus
    {
        Prepared = 0,     // 已准备
        Shipped = 1,      // 已发货
        Delivered = 2,    // 已送达
        Cancelled = 3     // 已取消
    }
}

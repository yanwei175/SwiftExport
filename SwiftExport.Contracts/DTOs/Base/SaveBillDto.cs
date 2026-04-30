using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Base
{
    // 泛型 DTO 基础结构
    public class SaveBillDto<THeader, TItem>
    {
        // 单据头（例如：订单号、日期、客户ID、备注）
        public THeader Header { get; set; }

        // 现有的（包含新增和修改）行列表
        public List<TItem> Items { get; set; }

        // 需要从数据库物理删除的行 ID 集合
        public List<int> DeletedItemIds { get; set; }
    }
}

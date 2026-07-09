using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.PurchaseOrders
{
    /// <summary>
    /// 采购订单详情 DTO（头 + 行）。
    /// </summary>
    public class PurchaseOrderDetailDto
    {
        public PurchaseOrderHeadDto Header { get; set; } = new PurchaseOrderHeadDto();
        public List<PurchaseOrderLineDto> Lines { get; set; } = new List<PurchaseOrderLineDto>();
        /// <summary>
        /// 扩展字段：专门用于保存时告知后端哪些行需要被物理删除
        /// 只有在“保存”请求时才会用到这个字段
        /// </summary>
        public List<Guid> DeletedLineIds { get; set; } = new List<Guid>();
    }
}

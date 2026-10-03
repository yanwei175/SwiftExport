using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListSourceSearchRequest : PagedRequestBase
    {
        /// <summary>必须指定最终客户。</summary>
        public Guid CustomerId { get; set; }
        /// <summary>可选标准产品身份；传入时仅查相同产品。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>可选非标筛选，空值不限制。</summary>
        public bool? IsNonStandard { get; set; }
        /// <summary>来源发票号精确筛选；销售 InvoiceNo 或系统售后单号，不查询 PO。</summary>
        public List<string> InvoiceNumbers { get; set; } = new List<string>();
        /// <summary>批量关键词，任一关键词命中即匹配；空集合不限制，最多 200 项，每项最多 200 字符。</summary>
        public List<string> Keywords { get; set; } = new List<string>();
    }
}

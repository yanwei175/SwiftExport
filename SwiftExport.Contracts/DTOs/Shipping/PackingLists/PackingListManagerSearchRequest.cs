using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListManagerSearchRequest : PagedRequestBase
    {
        /// <summary>客户筛选。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>空值表示全部过账状态。</summary>
        public PackingListPostingStatusEnum? PostingStatus { get; set; }
        /// <summary>单据日期起始，含当天。</summary>
        public DateTime? InvoiceDateFrom { get; set; }
        /// <summary>单据日期截止，含当天。</summary>
        public DateTime? InvoiceDateTo { get; set; }
    }
}

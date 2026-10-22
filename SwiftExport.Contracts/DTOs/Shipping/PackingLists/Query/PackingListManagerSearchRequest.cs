using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>箱单管理分页筛选；多选集合优先于旧单值，空集合表示不限。</summary>
    public class PackingListManagerSearchRequest : PagedRequestBase
    {
        /// <summary>客户筛选。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>客户多选；未提供时沿用 CustomerId，空集合不限制客户。</summary>
        public List<Guid> CustomerIds { get; set; }
        /// <summary>空值表示全部过账状态。</summary>
        public PackingListPostingStatusEnum? PostingStatus { get; set; }
        /// <summary>过账状态多选；未提供时沿用 PostingStatus，空集合不限制状态。</summary>
        public List<PackingListPostingStatusEnum> PostingStatuses { get; set; }
        /// <summary>单据日期起始，含当天。</summary>
        public DateTime? InvoiceDateFrom { get; set; }
        /// <summary>单据日期截止，含当天。</summary>
        public DateTime? InvoiceDateTo { get; set; }
    }
}

using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
namespace SwiftExport.Contracts.DTOs.OrderTracking.Sources
{
    /// <summary>一个稳定来源的全部非删除成品需求，不受主表关键词和分页限制。</summary>
    public class OrderTrackingSourceDemandsDto
    {
        /// <summary>来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>稳定来源组身份，缺失来源时回退需求身份。</summary>
        public Guid SourceGroupId { get; set; }
        /// <summary>完整来源需求集合，不截断。</summary>
        public List<OrderTrackingListItemDto> Items { get; set; } = new List<OrderTrackingListItemDto>();
    }
}

using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>单个来源全部非删除成品需求的摘要，不应用主表筛选或分页。</summary>
    public class OrderTrackingSourceDemandSummariesDto
    {
        /// <summary>响应来源类型。</summary>
        public string SourceType { get; set; }

        /// <summary>响应来源身份，供客户端拦截过期结果。</summary>
        public Guid SourceGroupId { get; set; }

        /// <summary>服务端中国标准时间业务日。</summary>
        public DateTime BusinessDate { get; set; }

        /// <summary>本次摘要读取时间。</summary>
        public DateTimeOffset ReadAt { get; set; }

        /// <summary>全部需求摘要，包含非活动需求但不计入来源当前进度。</summary>
        public List<OrderTrackingDemandSummaryDto> Items { get; set; } = new List<OrderTrackingDemandSummaryDto>();
    }
}

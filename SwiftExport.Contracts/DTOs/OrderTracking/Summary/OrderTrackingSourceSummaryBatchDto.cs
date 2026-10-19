using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>当前页来源摘要结果；时间只代表本次读取，不保证与其他请求共享快照。</summary>
    public class OrderTrackingSourceSummaryBatchDto
    {
        /// <summary>服务端中国标准时间业务日。</summary>
        public DateTime BusinessDate { get; set; }

        /// <summary>本次摘要读取时间。</summary>
        public DateTimeOffset ReadAt { get; set; }

        /// <summary>按来源稳定身份返回摘要。</summary>
        public List<OrderTrackingSourceSummaryDto> Items { get; set; } = new List<OrderTrackingSourceSummaryDto>();
    }
}

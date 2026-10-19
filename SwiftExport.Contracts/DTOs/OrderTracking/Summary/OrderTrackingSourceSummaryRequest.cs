using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>当前页来源摘要批量请求，不携带主表过滤条件。</summary>
    public class OrderTrackingSourceSummaryRequest
    {
        /// <summary>待查询来源，去重后最多200个；空集合返回空结果。</summary>
        public List<OrderTrackingSourceKeyDto> Sources { get; set; } = new List<OrderTrackingSourceKeyDto>();
    }
}

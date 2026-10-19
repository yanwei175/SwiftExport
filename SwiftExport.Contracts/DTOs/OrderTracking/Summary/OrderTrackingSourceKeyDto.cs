using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>来源摘要查询的稳定身份，不使用单号文字分组。</summary>
    public class OrderTrackingSourceKeyDto
    {
        /// <summary>来源类型SO、CR或MD。</summary>
        public string SourceType { get; set; }

        /// <summary>来源组身份，沿用基础查询的回退身份规则。</summary>
        public Guid SourceGroupId { get; set; }
    }
}

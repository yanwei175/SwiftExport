using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Sources
{
    /// <summary>指定来源内命中查询的完整成品需求身份；独立读取的当前事实，不代表主表查询时刻的快照。</summary>
    public class OrderTrackingSourceMatchesDto
    {
        /// <summary>来源类型，与请求的稳定身份对应。</summary>
        public string SourceType { get; set; } = string.Empty;
        /// <summary>来源稳定分组身份。</summary>
        public Guid SourceGroupId { get; set; }
        /// <summary>本次匹配使用的服务端业务日期，客户端不自行重算逾期。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>完整去重的命中成品需求身份；成功空集合才表示本次没有命中。</summary>
        public List<Guid> MatchedGrossDemandIds { get; set; } = new List<Guid>();
    }
}

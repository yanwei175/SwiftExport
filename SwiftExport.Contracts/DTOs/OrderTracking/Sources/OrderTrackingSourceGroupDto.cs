using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Sources
{
    /// <summary>来源主表行，数量是需求条数而不是混合单位数量合计。</summary>
    public class OrderTrackingSourceGroupDto
    {
        /// <summary>来源稳定身份；缺失时使用需求身份防止无关记录混组。</summary>
        public Guid SourceGroupId { get; set; }
        /// <summary>来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>原始来源身份，可能为空Guid。</summary>
        public Guid SourceUid { get; set; }
        /// <summary>来源单号显示，不用作分组身份。</summary>
        public string SourceNo { get; set; }
        /// <summary>组内唯一客户的当前代码，混合客户为空并说明原因。</summary>
        public string CustomerCode { get; set; }
        /// <summary>组内唯一客户当前名称。</summary>
        public string CustomerName { get; set; }
        /// <summary>当前SO头客户PO，其他来源为空。</summary>
        public string CustomerPo { get; set; }
        /// <summary>当前SO头发票号，其他来源为空。</summary>
        public string InvoiceNo { get; set; }
        /// <summary>组内全部非软删除需求条数，不受筛选影响。</summary>
        public int TotalDemandCount { get; set; }
        /// <summary>组内符合当前筛选的需求条数。</summary>
        public int MatchedDemandCount { get; set; }
        /// <summary>来源身份、单号或客户不一致等需核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}

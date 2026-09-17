using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>过账或反向阻断，支持定位到来源及物理箱单行。</summary>
    public class PackingListPostingBlockerDto
    {
        /// <summary>稳定错误码。</summary>
        public string Code { get; set; }
        /// <summary>可展示的阻断说明。</summary>
        public string Message { get; set; }
        /// <summary>来源身份；整单错误为空。</summary>
        public Guid? GrossDemandId { get; set; }
        /// <summary>可定位时提供物理行身份。</summary>
        public Guid? PackingListLineId { get; set; }
        /// <summary>可计算的缺口；不可确定时为空。</summary>
        public decimal? ShortfallQty { get; set; }
    }
}

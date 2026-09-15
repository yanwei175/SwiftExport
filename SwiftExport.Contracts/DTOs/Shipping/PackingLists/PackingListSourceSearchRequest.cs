using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListSourceSearchRequest : PagedRequestBase
    {
        /// <summary>必须指定最终客户。</summary>
        public Guid CustomerId { get; set; }
    }
}

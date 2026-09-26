using System;
using System.Collections.Generic;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>按指定仓库查询正库存；排除本单已选择的产品，不扣减其他草稿。</summary>
    public class OutboundItemSearchRequest : PagedRequestBase
    {
        public Guid WarehouseSiteId { get; set; }
        public List<Guid> ExcludedProductIds { get; set; } = new List<Guid>();
    }
}

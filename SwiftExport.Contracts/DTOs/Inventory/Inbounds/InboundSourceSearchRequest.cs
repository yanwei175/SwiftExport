using System;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>按入库类型分页查询来源，不按仓库筛选；供应商身份来自全量缓存，不使用有效供应商专用查询。</summary>
    public class InboundSourceSearchRequest : PagedRequestBase
    {
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public InboundPurchaseCategoryEnum? PurchaseCategory { get; set; }
        public Guid? SupplierId { get; set; }
    }
}

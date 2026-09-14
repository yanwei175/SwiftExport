using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>入库管理分页筛选；不承担选源或过账参数。</summary>
    public class InboundManagerSearchRequest : PagedRequestBase
    {
        public InboundDocumentTypeEnum? DocumentType { get; set; }
    }
}


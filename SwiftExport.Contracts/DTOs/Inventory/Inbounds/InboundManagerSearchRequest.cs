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

        /// <summary>按过账状态筛选；为空时查询所有状态，不执行过账操作。</summary>
        public InboundPostingStatusEnum? PostingStatus { get; set; }
    }
}

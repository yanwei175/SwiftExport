using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query
{
    /// <summary>入库管理分页筛选；不承担选源或过账参数。</summary>
    public class InboundManagerSearchRequest : PagedRequestBase
    {
        /// <summary>旧单选类型条件；仅在 DocumentTypes 未提供时生效。</summary>
        public InboundDocumentTypeEnum? DocumentType { get; set; }

        /// <summary>入库类型并集；空集合表示不限，未提供时沿用旧单选条件。</summary>
        public List<InboundDocumentTypeEnum> DocumentTypes { get; set; }

        /// <summary>旧单选状态条件；仅在 PostingStatuses 未提供时生效，为空表示不限。</summary>
        public InboundPostingStatusEnum? PostingStatus { get; set; }

        /// <summary>过账状态并集；空集合表示不限，未提供时沿用旧单选条件，不执行过账操作。</summary>
        public List<InboundPostingStatusEnum> PostingStatuses { get; set; }
    }
}

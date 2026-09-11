using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览的警告或阻断信息。</summary>
    public class PurchaseDraftWarningDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public bool IsBlocker { get; set; }
        public bool RequiresConfirmation { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public Guid? SupplierId { get; set; }
        public List<Guid> NetDemandIds { get; set; } = new List<Guid>();
        /// <summary>受警告或确认范围覆盖的结构化物料摘要；包含未被本次勾选但受关联规则影响的对象。</summary>
        public List<PurchaseDraftWarningAffectedObjectDto> AffectedObjects { get; set; } = new List<PurchaseDraftWarningAffectedObjectDto>();
    }
}

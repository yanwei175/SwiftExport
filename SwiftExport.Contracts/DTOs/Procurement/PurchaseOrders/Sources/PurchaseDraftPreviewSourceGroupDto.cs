using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览中按来源聚合的结果。</summary>
    public class PurchaseDraftPreviewSourceGroupDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public string SourcePreviewToken { get; set; }
        public bool CanCreate { get; set; }
        public int DocumentCount { get; set; }
        public List<PurchaseDraftPreviewDocumentDto> Documents { get; set; } = new List<PurchaseDraftPreviewDocumentDto>();
        public List<PurchaseDraftWarningDto> Warnings { get; set; } = new List<PurchaseDraftWarningDto>();
    }
}

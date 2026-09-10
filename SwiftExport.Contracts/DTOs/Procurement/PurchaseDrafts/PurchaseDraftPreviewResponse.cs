using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览结果，包含来源、单据、警告和跳过项。</summary>
    public class PurchaseDraftPreviewResponse
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; }
        public string Currency { get; set; }
        public string PreviewToken { get; set; }
        public bool HasBlockers { get; set; }
        public bool RequiresConfirmation { get; set; }
        public List<PurchaseDraftPreviewSourceGroupDto> SourceGroups { get; set; } = new List<PurchaseDraftPreviewSourceGroupDto>();
        public List<PurchaseDraftWarningDto> Warnings { get; set; } = new List<PurchaseDraftWarningDto>();
        public List<PurchaseDraftSkippedItemDto> SkippedItems { get; set; } = new List<PurchaseDraftSkippedItemDto>();
    }
}

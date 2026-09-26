using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>采购草稿预览请求。服务端仍会重新读取活动计划和剩余量。</summary>
    public class PurchaseDraftPreviewRequest
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; } = PurchaseDraftLineModeEnum.MergeCompatibleLines;
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public List<PurchaseDraftPreviewItem> Items { get; set; } = new List<PurchaseDraftPreviewItem>();
        public List<PurchaseDraftTargetSelectionDto> Targets { get; set; } = new List<PurchaseDraftTargetSelectionDto>();
    }
}

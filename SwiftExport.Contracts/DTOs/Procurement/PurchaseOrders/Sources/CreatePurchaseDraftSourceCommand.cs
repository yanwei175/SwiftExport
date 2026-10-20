using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>按来源执行采购草稿创建的命令分组。</summary>
    public class CreatePurchaseDraftSourceCommand
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; } = PurchaseDraftLineModeEnum.MergeCompatibleLines;
        public List<CreatePurchaseDraftItemCommand> Items { get; set; } = new List<CreatePurchaseDraftItemCommand>();
        public List<CreatePurchaseDraftTargetCommand> Targets { get; set; } = new List<CreatePurchaseDraftTargetCommand>();
    }
}

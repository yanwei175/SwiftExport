using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>预览确认后的采购草稿创建命令；服务端必须重新锁定并校验事实。</summary>
    public class CreatePurchaseDraftsCommand
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public string PreviewToken { get; set; }
        public List<CreatePurchaseDraftSourceCommand> Sources { get; set; } = new List<CreatePurchaseDraftSourceCommand>();
    }
}


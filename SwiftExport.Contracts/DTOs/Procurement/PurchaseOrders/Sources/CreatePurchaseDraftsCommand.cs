using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>采购准备页直接创建草稿的命令；服务端锁定需求并重新校验身份、版本和可创建容量。</summary>
    public class CreatePurchaseDraftsCommand
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public List<CreatePurchaseDraftSourceCommand> Sources { get; set; } = new List<CreatePurchaseDraftSourceCommand>();
    }
}


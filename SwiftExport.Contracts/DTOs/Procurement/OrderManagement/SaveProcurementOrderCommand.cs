using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>仅保存未确认订单的商务白名单，不改变身份、数量、送达供应商或行集合。</summary>
    public sealed class SaveProcurementOrderCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Remark { get; set; }
        public string PackagingRequirements { get; set; }
        public List<SaveProcurementOrderLineCommand> Lines { get; set; } = new List<SaveProcurementOrderLineCommand>();
    }
}

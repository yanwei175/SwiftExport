using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>按服务端确认上下文指纹确认订单，不绑定库存仓库。</summary>
    public sealed class ConfirmProcurementOrderCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string ContextFingerprint { get; set; }
    }
}

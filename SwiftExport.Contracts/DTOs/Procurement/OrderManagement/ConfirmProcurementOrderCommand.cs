using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>按服务端确认上下文指纹确认订单，显式绑定各客供件路径的消耗仓库。</summary>
    public sealed class ConfirmProcurementOrderCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string ContextFingerprint { get; set; }
        public List<ProcurementConsumptionBinding> ConsumptionBindings { get; set; } = new List<ProcurementConsumptionBinding>();
    }
}

using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace
{
    /// <summary>单条成品需求的只读链路证据；数量只在各自单位和事实粒度内解释。</summary>
    public class GrossDemandTraceDetailDto
    {
        public Guid GrossDemandId { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public string CustomerName { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Unit { get; set; }
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>仅可信的已过账、未反向装箱行合计；单位或客户证据异常时为空。</summary>
        public decimal? DeliveredQty { get; set; }
        public string ReviewReason { get; set; }
        public List<GrossDemandTraceMaterialDto> Materials { get; set; } = new List<GrossDemandTraceMaterialDto>();
        public List<GrossDemandTracePurchaseDto> Purchases { get; set; } = new List<GrossDemandTracePurchaseDto>();
        public List<GrossDemandTraceDeliveryDto> Deliveries { get; set; } = new List<GrossDemandTraceDeliveryDto>();
    }
}

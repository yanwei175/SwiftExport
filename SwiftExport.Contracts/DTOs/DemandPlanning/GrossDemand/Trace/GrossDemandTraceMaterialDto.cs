using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace
{
    /// <summary>成品需求一次拆解修订中的物料行及当前可证明的采购贡献。</summary>
    public class GrossDemandTraceMaterialDto
    {
        public Guid NetDemandId { get; set; }
        public int DecompositionRevision { get; set; }
        public int LineNumber { get; set; }
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        public string Sku { get; set; }
        public string MaterialType { get; set; }
        public decimal NetQty { get; set; }
        public decimal? ActivePurchaseQty { get; set; }
        public string ReviewReason { get; set; }
    }
}

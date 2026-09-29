using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace
{
    /// <summary>同一拆解树中可定位的物料行。</summary>
    public class MaterialDemandTraceTreeItemDto
    {
        /// <summary>物料行身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>父行身份。</summary>
        public Guid? ParentNetDemandId { get; set; }
        /// <summary>拆解修订号。</summary>
        public int DecompositionRevision { get; set; }
        /// <summary>行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>生命周期。</summary>
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>SKU 快照。</summary>
        public string Sku { get; set; }
        /// <summary>需求量。</summary>
        public decimal NetQty { get; set; }
    }
}

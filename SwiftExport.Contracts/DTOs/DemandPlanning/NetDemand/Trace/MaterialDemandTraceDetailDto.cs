using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace
{
    /// <summary>单条物料需求的来源、拆解与采购执行只读证据。</summary>
    public class MaterialDemandTraceDetailDto
    {
        /// <summary>物料需求身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>来源需求身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>来源单号。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源行号。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>拆解修订号。</summary>
        public int DecompositionRevision { get; set; }
        /// <summary>拆解行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>生命周期。</summary>
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>物料 SKU 快照。</summary>
        public string Sku { get; set; }
        /// <summary>可证明的物料单位；无法证明时为空。</summary>
        public string Unit { get; set; }
        /// <summary>净需求量。</summary>
        public decimal NetQty { get; set; }
        /// <summary>需求交期。</summary>
        public DateTime? RequiredDate { get; set; }
        /// <summary>拆解回退时间。</summary>
        public DateTime? RolledBackAt { get; set; }
        /// <summary>拆解回退人。</summary>
        public string RolledBackBy { get; set; }
        /// <summary>拆解回退原因。</summary>
        public string RolledBackReason { get; set; }
        /// <summary>父级物料需求。</summary>
        public Guid? ParentNetDemandId { get; set; }
        /// <summary>根物料需求。</summary>
        public Guid RootNetDemandId { get; set; }
        /// <summary>有效承接量；证据不足时为空。</summary>
        public decimal? AssociatedPurchaseQty { get; set; }
        /// <summary>当前收货覆盖；证据不足或历史行时为空。</summary>
        public decimal? CurrentReceiptCoverageQty { get; set; }
        /// <summary>可信度提示。</summary>
        public string ReviewReason { get; set; }
        /// <summary>同棵拆解树内的修订行。</summary>
        public List<MaterialDemandTraceTreeItemDto> TreeItems { get; set; } = new List<MaterialDemandTraceTreeItemDto>();
        /// <summary>本行活动与历史采购关系。</summary>
        public List<MaterialDemandTracePurchaseDto> Purchases { get; set; } = new List<MaterialDemandTracePurchaseDto>();
    }
}

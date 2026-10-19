using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Materials
{
    /// <summary>物料需求修订与采用时快照，不查询采购或收货。</summary>
    public class DemandTrackingMaterialDto
    {
        /// <summary>物料需求身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>所属根物料身份。</summary>
        public Guid RootNetDemandId { get; set; }
        /// <summary>父件身份，根为空。</summary>
        public Guid? ParentNetDemandId { get; set; }
        /// <summary>采用修订号，不与其他修订累加。</summary>
        public int DecompositionRevision { get; set; }
        /// <summary>修订内行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>物料生命周期。</summary>
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>历史软删除标记。</summary>
        public bool IsDeleted { get; set; }
        /// <summary>物料编码快照。</summary>
        public string Sku { get; set; }
        /// <summary>物料描述快照。</summary>
        public string Description { get; set; }
        /// <summary>物料类型快照。</summary>
        public string MaterialType { get; set; }
        /// <summary>供货方式快照。</summary>
        public string SupplyType { get; set; }
        /// <summary>物料单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>采用组成计算数量。</summary>
        public decimal GrossQty { get; set; }
        /// <summary>当前物料需求数量，不推断已扣库存。</summary>
        public decimal NetQty { get; set; }
        /// <summary>物料保存交期。</summary>
        public DateTime? RequiredDate { get; set; }
        /// <summary>来源修订或快照证据需核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}

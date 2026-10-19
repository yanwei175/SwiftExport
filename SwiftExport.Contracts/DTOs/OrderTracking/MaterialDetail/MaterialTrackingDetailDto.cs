using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
namespace SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail
{
    /// <summary>单条物料需求的独立只读快照，历史修订仍可定位。</summary>
    public class MaterialTrackingDetailDto
    {
        /// <summary>当前物料需求身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>所属成品需求身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>成品需求来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>成品需求来源单号。</summary>
        public string SourceNo { get; set; }
        /// <summary>成品需求来源行号。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>当前客户显示代码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>物料快照和修订事实。</summary>
        public DemandTrackingMaterialDto Material { get; set; }
        /// <summary>父物料编码快照。</summary>
        public string ParentSku { get; set; }
        /// <summary>客户型号快照。</summary>
        public string CustomerSku { get; set; }
        /// <summary>颜色快照。</summary>
        public string Color { get; set; }
        /// <summary>规格快照。</summary>
        public string Specification { get; set; }
        /// <summary>采用的BOM路径快照。</summary>
        public string BomPath { get; set; }
        /// <summary>采用的BOM版本标记。</summary>
        public int? BomVersion { get; set; }
        /// <summary>物料备注。</summary>
        public string Remark { get; set; }
        /// <summary>拆解回退时间。</summary>
        public DateTime? RolledBackAt { get; set; }
        /// <summary>拆解回退人。</summary>
        public string RolledBackBy { get; set; }
        /// <summary>拆解回退原因。</summary>
        public string RolledBackReason { get; set; }
    }
}

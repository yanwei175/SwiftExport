using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
namespace SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail
{
    /// <summary>单个物料的采购承接进度与关联证据，独立于收货权限和历史校验。</summary>
    public class MaterialTrackingPurchaseSectionDto
    {
        /// <summary>物料身份。</summary>
        public Guid NetDemandId { get; set; }
        /// <summary>成品需求身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>该物料保存的需求量。</summary>
        public decimal NetQty { get; set; }
        /// <summary>物料单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>可信活动采购净贡献，历史或证据异常为未知。</summary>
        public decimal? AssociatedPurchaseQty { get; set; }
        /// <summary>需求量减可信承接量，保留异常负数供核实。</summary>
        public decimal? RemainingToPurchaseQty { get; set; }
        /// <summary>承接数量核实原因。</summary>
        public string ReviewReason { get; set; }
        /// <summary>本物料活动及历史关联，一行一个Link。</summary>
        public List<DemandTrackingPurchaseDto> Items { get; set; } = new List<DemandTrackingPurchaseDto>();
    }
}

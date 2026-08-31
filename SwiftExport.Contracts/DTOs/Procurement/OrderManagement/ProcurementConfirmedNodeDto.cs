using System;
using SwiftExport.Contracts.DTOs.Products.Execution;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>已冻结节点的严格结构与明确消耗目标；结构比例语义与 BOM 执行节点一致。</summary>
    public sealed class ProcurementConfirmedNodeDto
    {
        public BomExecutionNodeDto Structure { get; set; }
        public Guid? ConsumptionWarehouseSiteId { get; set; }
        public string WarehouseCodeSnapshot { get; set; }
        public string WarehouseNameSnapshot { get; set; }
        public Guid? ConsumptionSupplierId { get; set; }
        public string SupplierCodeSnapshot { get; set; }
        public string SupplierNameSnapshot { get; set; }
    }
}

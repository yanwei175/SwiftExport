using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class NetDemandTraceItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid GrossDemandId { get; set; }
        public int DecompositionRevision { get; set; }
        public int LineNumber { get; set; }
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }

        public Guid? ParentNetDemandId { get; set; }
        public string ParentSku { get; set; }
        public Guid RootNetDemandId { get; set; }
        public string RootSku { get; set; }
        public Guid? DeliveryTargetNetDemandId { get; set; }
        public string DeliveryTargetSku { get; set; }
        public DemandDeliveryModeEnum DeliveryMode { get; set; }

        public Guid? ProductId { get; set; }
        public Guid? BomLineId { get; set; }
        public int? BomVersion { get; set; }
        public string BomPath { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }

        public decimal GrossQty { get; set; }
        public decimal NetQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public Guid? PreferredSupplierId { get; set; }
        public string PreferredSupplierCode { get; set; }
        public DemandExecutionProgressEnum ExecutionProgress { get; set; }

        public DateTime? RolledBackAt { get; set; }
        public string RolledBackBy { get; set; }
        public string RolledBackReason { get; set; }
    }
}

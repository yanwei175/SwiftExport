using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 净需求工作台分页查询条件。
    /// </summary>
    public class NetDemandSearchRequest : PagedRequestBase
    {
        /// <summary>
        /// 来源导航选中的唯一来源标识；填写后服务端返回该来源的全部匹配净需求，不执行分页。
        /// </summary>
        public Guid? SourceUid { get; set; }

        public List<Guid> NetDemandIds { get; set; } = new List<Guid>();
        public List<Guid> GrossDemandIds { get; set; } = new List<Guid>();
        public List<Guid> SourceUids { get; set; } = new List<Guid>();
        public List<string> SourceTypes { get; set; } = new List<string>();
        public List<string> SourceNos { get; set; } = new List<string>();
        public List<string> Skus { get; set; } = new List<string>();
        public List<string> CustomerSkus { get; set; } = new List<string>();
        public DateTime? DemandRequiredDateFrom { get; set; }
        public DateTime? DemandRequiredDateTo { get; set; }
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
        public List<DemandExecutionTypeEnum> ExecutionTypes { get; set; } = new List<DemandExecutionTypeEnum>();
        public List<NetDemandPlanStatusEnum> PlanStatuses { get; set; } = new List<NetDemandPlanStatusEnum>();
        public List<Guid> PurchaseSupplierIds { get; set; } = new List<Guid>();
        public List<Guid> DeliveryTargetSupplierIds { get; set; } = new List<Guid>();
        public bool? IsCustomerSupplied { get; set; }
        public bool? IsNonStandard { get; set; }
        public bool? HasRemainingQty { get; set; }
    }
}

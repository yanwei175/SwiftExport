using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求工作台分页查询条件，不包含计划、分配或转单状态筛选。</summary>
    public class MaterialDemandWorkspaceSearchRequest : PagedRequestBase
    {
        /// <summary>来源导航选中的唯一来源；填写后返回该来源全部活动物料需求。</summary>
        public Guid? SourceUid { get; set; }
        /// <summary>限定物料需求 Uid。</summary>
        public List<Guid> NetDemandIds { get; set; } = new List<Guid>();
        /// <summary>限定来源需求 Uid。</summary>
        public List<Guid> GrossDemandIds { get; set; } = new List<Guid>();
        /// <summary>限定来源业务单据 Uid。</summary>
        public List<Guid> SourceUids { get; set; } = new List<Guid>();
        /// <summary>限定来源业务类型。</summary>
        public List<string> SourceTypes { get; set; } = new List<string>();
        /// <summary>限定来源业务单号。</summary>
        public List<string> SourceNos { get; set; } = new List<string>();
        /// <summary>限定物料 SKU。</summary>
        public List<string> Skus { get; set; } = new List<string>();
        /// <summary>限定客户型号。</summary>
        public List<string> CustomerSkus { get; set; } = new List<string>();
        /// <summary>来源需求交期起点。</summary>
        public DateTime? DemandRequiredDateFrom { get; set; }
        /// <summary>来源需求交期终点。</summary>
        public DateTime? DemandRequiredDateTo { get; set; }
        /// <summary>限定父件或客供件；为空表示全部。</summary>
        public bool? IsCustomerSupplied { get; set; }
        /// <summary>限定标准或非标物料；为空表示全部。</summary>
        public bool? IsNonStandard { get; set; }
    }
}

using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求工作台分页查询条件，不包含计划、分配或转单状态筛选。</summary>
    public class MaterialDemandManagerSearchRequest : PagedRequestBase
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
        /// <summary>物料生命周期；为空时仅查询活动行。</summary>
        public SwiftExport.Contracts.SysEnums.NetDemandLifecycleStatusEnum? LifecycleStatus { get; set; }
        /// <summary>单选快捷视图：All、Unassigned、Partial、Assigned、Overdue、DueSoon、Review。</summary>
        public string QuickView { get; set; } = "All";
        /// <summary>推荐采购供应商 Uid。</summary>
        public Guid? SuggestedPurchaseSupplierId { get; set; }
        /// <summary>按推荐供应商编码或名称模糊查询。</summary>
        public string SuggestedPurchaseSupplierKeyword { get; set; }
        /// <summary>有效采购单实际供应商 Uid。</summary>
        public Guid? ActualPurchaseSupplierId { get; set; }
        /// <summary>按实际采购供应商编码或名称模糊查询。</summary>
        public string ActualPurchaseSupplierKeyword { get; set; }
        /// <summary>采购单号精确匹配。</summary>
        public string PurchaseOrderNo { get; set; }
    }
}

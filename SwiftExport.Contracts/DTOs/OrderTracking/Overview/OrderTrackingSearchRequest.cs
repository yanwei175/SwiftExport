using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Overview
{
    /// <summary>成品需求行分页条件；Keyword统一匹配来源单号、客户PO、发票号及产品、客户、中性型号。</summary>
    public class OrderTrackingSearchRequest : PagedRequestBase
    {
        /// <summary>来源默认SO；空集合不限SO/CR/MD。</summary>
        public List<string> SourceTypes { get; set; } = new List<string> { "SO" };
        /// <summary>当前客户代码精确多选，空集合不限。</summary>
        public List<string> CustomerCodes { get; set; } = new List<string>();
        /// <summary>要求交期起日，含当天。</summary>
        public DateTime? RequiredDateFrom { get; set; }
        /// <summary>要求交期止日，含当天；空交期不匹配。</summary>
        public DateTime? RequiredDateTo { get; set; }
        /// <summary>生命周期集合内并集，空集合不限。</summary>
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
        /// <summary>选定来源组类型，与SourceGroupId成对；为空表示不限定单组。</summary>
        public string SourceGroupType { get; set; }
        /// <summary>来源组稳定键；来源身份缺失时以需求身份隔离，不按单号文字混组。</summary>
        public Guid? SourceGroupId { get; set; }
    }
}

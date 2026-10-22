using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购订单事实分页条件；不接受货好、收货或跟进进度筛选，空状态集合不限制对应状态。</summary>
    public sealed class PurchaseOrderManagerSearchRequest : PagedRequestBase
    {
        /// <summary>关键词与客户编码的最大字符数，供查询输入和服务端校验共用。</summary>
        public const int MaxTextLength = 100;
        /// <summary>客户编码和供应商身份多选集合各自的最大项数。</summary>
        public const int MaxPartyFilterCount = 500;
        /// <summary>单次查询最多返回的采购订单条数。</summary>
        public const int MaxPageSize = 200;

        /// <summary>按采购头保存的客户编码精确筛选，不联查客户主数据。</summary>
        public string CustomerCode { get; set; }
        public Guid? SupplierId { get; set; }
        /// <summary>采购头客户代码快照多选；集合内取并集，空集合不限制，与旧单值条件取交集。</summary>
        public List<string> CustomerCodes { get; set; } = new List<string>();
        /// <summary>实际供应商身份多选；集合内取并集，空集合不限制，与旧单值条件取交集。</summary>
        public List<Guid> SupplierIds { get; set; } = new List<Guid>();
        /// <summary>关联页面仅查询活动且仍有未关联数量的采购单；在计数和分页前筛选，默认不限制管理列表。</summary>
        public bool OnlyWithUnlinkedQuantity { get; set; }
        /// <summary>下单日期起止均按自然日包含边界；空值表示不限制。</summary>
        public DateTime? OrderDateFrom { get; set; }
        public DateTime? OrderDateTo { get; set; }
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
    }
}

using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Manager
{
    /// <summary>
    /// 销售订单管理的订单事实摘要，不包含需求执行或交付进度。
    /// </summary>
    /// <remarks>来源表：SalesOrderHeader、Customer、SalesOrderLine。</remarks>
    public class SalesOrderManagerListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public SalesOrderDocumentStatusEnum DocumentStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public string OrderNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPoNo { get; set; }
        public string InvoiceNo { get; set; }
        public string Currency { get; set; }
        /// <summary>已弃用的兼容查询字段，原样保留历史值，不参与销售业务或价格换算。</summary>
        public decimal? ExchangeRate { get; set; }
        /// <summary>活动订单行同单位且单位完整时的订购数量；混合或缺失单位时为空，空单为零，不受下游执行影响。</summary>
        public decimal? TotalQty { get; set; }
        /// <summary>可汇总订单数量的单位代码；空单、多单位或缺失单位时为空。</summary>
        public string UnitSnapshot { get; set; }
        /// <summary>活动订单行包含多个不同的非空单位，不能跨单位合计数量。</summary>
        public bool HasMixedUnits { get; set; }
        /// <summary>至少一条活动订单行缺少单位快照，不能解释合计数量。</summary>
        public bool HasMissingUnit { get; set; }
        public decimal TotalAmountOriginalCurrency { get; set; }
        public decimal TotalAmountLocalCurrency { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}

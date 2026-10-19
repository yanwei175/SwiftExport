using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail
{
    /// <summary>成品需求跟踪基础明细，独立于物料、采购和交付区域加载。</summary>
    public class DemandTrackingHeaderDto
    {
        /// <summary>成品需求稳定身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>当前需求版本，仅用于展示和查询身份。</summary>
        public int Version { get; set; }
        /// <summary>来源类型 SO、CR 或 MD。</summary>
        public string SourceType { get; set; }
        /// <summary>来源单据头身份。</summary>
        public Guid SourceUid { get; set; }
        /// <summary>来源单据行身份。</summary>
        public Guid SourceLineId { get; set; }
        /// <summary>成品需求保存的来源单号快照。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源行号快照。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>成品需求客户身份，历史缺失时为空。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>当前客户代码，仅作显示定位。</summary>
        public string CustomerCode { get; set; }
        /// <summary>当前客户名称，仅作显示定位。</summary>
        public string CustomerName { get; set; }
        /// <summary>销售来源当前订单的客户 PO，其他来源为空。</summary>
        public string CustomerPo { get; set; }
        /// <summary>销售来源当前订单的发票号，其他来源为空。</summary>
        public string InvoiceNo { get; set; }
        /// <summary>产品身份，非标允许为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>是否非标需求。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>成品需求产品编码快照。</summary>
        public string Sku { get; set; }
        /// <summary>成品需求客户型号快照。</summary>
        public string CustomerSku { get; set; }
        /// <summary>成品需求中性型号快照。</summary>
        public string LinkSku { get; set; }
        /// <summary>成品需求图片路径快照。</summary>
        public string ImagePath { get; set; }
        /// <summary>成品需求描述快照。</summary>
        public string Description { get; set; }
        /// <summary>成品需求颜色快照。</summary>
        public string Color { get; set; }
        /// <summary>成品需求规格快照。</summary>
        public string Specification { get; set; }
        /// <summary>需求自身数量，不受交期或下游查询影响。</summary>
        public decimal DemandQty { get; set; }
        /// <summary>成品需求单位快照，缺失不从主数据补写。</summary>
        public string Unit { get; set; }
        /// <summary>成品需求保存的要求交期，空值表示未指定。</summary>
        public DateTime? RequiredDate { get; set; }
        /// <summary>成品需求自身生命周期，不表示履约进度。</summary>
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>接收时来源版本。</summary>
        public int? SourceVersion { get; set; }
        /// <summary>成品需求备注快照。</summary>
        public string Remark { get; set; }
        /// <summary>最近生命周期变更时间。</summary>
        public DateTime? LifecycleChangedAt { get; set; }
        /// <summary>最近生命周期变更操作人。</summary>
        public string LifecycleChangedBy { get; set; }
        /// <summary>最近生命周期变更原因。</summary>
        public string LifecycleChangedReason { get; set; }
        /// <summary>成品需求建立时间。</summary>
        public DateTime? CreatedDate { get; set; }
        /// <summary>成品需求建立操作人。</summary>
        public string CreatedBy { get; set; }
        /// <summary>成品需求最近修改时间。</summary>
        public DateTime? UpdatedDate { get; set; }
        /// <summary>成品需求最近修改操作人。</summary>
        public string UpdatedBy { get; set; }
    }
}

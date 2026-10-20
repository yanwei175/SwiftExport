using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders
{
    /// <summary>指定采购单全量有效行及允许读取的进度，不受主表分页或进度筛选裁剪。</summary>
    public sealed class PurchaseOrderTrackingLineDto
    {
        /// <summary>采购行稳定身份。</summary>
        public Guid PurchaseOrderLineId { get; set; }
        /// <summary>采购行读取时版本。</summary>
        public int Version { get; set; }
        /// <summary>实际产品身份。</summary>
        public Guid ProductId { get; set; }
        /// <summary>采购行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>当前产品图片投影，不是采购历史图片快照。</summary>
        public string MainImage { get; set; }
        /// <summary>物料编码快照。</summary>
        public string Sku { get; set; }
        /// <summary>客户型号快照。</summary>
        public string CustomerSku { get; set; }
        /// <summary>中性关联型号快照。</summary>
        public string LinkSku { get; set; }
        /// <summary>供应商型号快照。</summary>
        public string SupplierSku { get; set; }
        /// <summary>中文描述快照。</summary>
        public string DescriptionZh { get; set; }
        /// <summary>表面处理快照。</summary>
        public string FinishZh { get; set; }
        /// <summary>配置快照。</summary>
        public string FeaturesZh { get; set; }
        /// <summary>物料类型快照。</summary>
        public string MaterialType { get; set; }
        /// <summary>供货方式快照.</summary>
        public string SupplyType { get; set; }
        /// <summary>采购整行数量，不表示任一需求的贡献份额。</summary>
        public decimal Quantity { get; set; }
        /// <summary>采购行保存的单位。</summary>
        public string UnitSnapshot { get; set; }
        /// <summary>采购行保存的计划交期。</summary>
        public DateTime? PlannedDeliveryDate { get; set; }
        /// <summary>是否有供应商反馈查看权限。</summary>
        public bool CanViewProgress { get; set; }
        /// <summary>是否有正式收货查看权限。</summary>
        public bool CanViewReceipts { get; set; }
        /// <summary>最新已知累计货好量，无权或未知时为空。</summary>
        public decimal? ReadyQty { get; set; }
        /// <summary>反馈预计完成日期。</summary>
        public DateTime? ExpectedCompletionDate { get; set; }
        /// <summary>最新反馈时间。</summary>
        public DateTime? LatestFollowUpDate { get; set; }
        /// <summary>最新反馈人员。</summary>
        public string FollowUpUserName { get; set; }
        /// <summary>采购整行可信正式净收货量，不分摊为需求收货。</summary>
        public decimal? ReceivedQty { get; set; }
        /// <summary>可信采购量扣除净收货后的剩余量；异常或无权时为空。</summary>
        public decimal? UnreceivedQty { get; set; }
        /// <summary>本行收货证据核实原因，无权时不暴露。</summary>
        public string ReceiptReviewReason { get; set; }
        /// <summary>有效需求贡献合计，无法核实时为空。</summary>
        public decimal? LinkedDemandQty { get; set; }
        /// <summary>未关联需求数量，无法核实时为空。</summary>
        public decimal? UnlinkedQty { get; set; }
        /// <summary>活动贡献关联的不重复物料需求数。</summary>
        public int LinkedDemandCount { get; set; }
        /// <summary>关联摘要状态，不代替订单生命周期。</summary>
        public PurchaseDemandAssociationStatusEnum AssociationStatus { get; set; }
        /// <summary>来源身份与贡献守恒核实原因。</summary>
        public string AssociationReviewReason { get; set; }
        /// <summary>全部有效贡献关系的来源概况。</summary>
        public string SourceSummary { get; set; }
        /// <summary>未税单价快照。</summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>含税单价快照。</summary>
        public decimal? TaxPrice { get; set; }
        /// <summary>未税金额快照。</summary>
        public decimal? UnitAmount { get; set; }
        /// <summary>含税金额快照。</summary>
        public decimal? TaxAmount { get; set; }
        /// <summary>客供送达供应商名称。</summary>
        public string DeliverySupplierName { get; set; }
        /// <summary>采购行保存的来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>采购行保存的来源单号。</summary>
        public string SourceNo { get; set; }
        /// <summary>采购行保存的来源行号。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>采购行备注。</summary>
        public string SubNote { get; set; }
    }
}

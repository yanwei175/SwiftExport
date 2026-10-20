using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>整个来源活动需求的摘要；需求计数与采购行计数分开，不累加不同单位数量。</summary>
    public class OrderTrackingSourceSummaryDto
    {
        /// <summary>来源类型。</summary>
        public string SourceType { get; set; }

        /// <summary>来源稳定身份。</summary>
        public Guid SourceGroupId { get; set; }

        /// <summary>全部非删除活动成品需求条数，不受主表局部筛选影响。</summary>
        public int ActiveDemandCount { get; set; }

        /// <summary>此维度读取状态：Readable、Forbidden或Failed；无权或失败时对应数量为空。</summary>
        public string DeliveryAvailability { get; set; }

        /// <summary>此维度无法读取的说明；不得包含无权数据。</summary>
        public string DeliveryMessage { get; set; }

        /// <summary>此维度读取状态：Readable、Forbidden或Failed；无权或失败时对应数量为空。</summary>
        public string MaterialAvailability { get; set; }

        /// <summary>此维度无法读取的说明；不得包含无权数据。</summary>
        public string MaterialMessage { get; set; }

        /// <summary>此维度读取状态：Readable、Forbidden或Failed；无权或失败时对应数量为空。</summary>
        public string PurchaseAvailability { get; set; }

        /// <summary>此维度无法读取的说明；不得包含无权数据。</summary>
        public string PurchaseMessage { get; set; }

        /// <summary>此维度读取状态：Readable、Forbidden或Failed；无权或失败时对应数量为空。</summary>
        public string ReceiptAvailability { get; set; }

        /// <summary>此维度无法读取的说明；不得包含无权数据。</summary>
        public string ReceiptMessage { get; set; }

        /// <summary>可信剩余未交等于零的活动需求条数。</summary>
        public int? DeliveryCompletedDemandCount { get; set; }

        /// <summary>可信剩余未交大于零的活动需求条数。</summary>
        public int? DeliveryOutstandingDemandCount { get; set; }

        /// <summary>交付数量证据需核实的活动需求条数。</summary>
        public int? DeliveryReviewDemandCount { get; set; }

        /// <summary>确定逾期活动需求条数。</summary>
        public int? OverdueDemandCount { get; set; }

        /// <summary>确定逾期的最长天数，无确定逾期时为零。</summary>
        public int? MaxOverdueDays { get; set; }

        /// <summary>存在活动物料的活动需求条数。</summary>
        public int? MaterialEstablishedDemandCount { get; set; }

        /// <summary>没有活动物料的活动需求条数，仅陈述事实不推断漏建。</summary>
        public int? NoActiveMaterialDemandCount { get; set; }

        /// <summary>当前物料依据需核实的活动需求条数。</summary>
        public int? MaterialReviewDemandCount { get; set; }

        /// <summary>存在适用采购物料的活动需求条数。</summary>
        public int? PurchaseApplicableDemandCount { get; set; }

        /// <summary>至少一个可信适用物料已确认采购不足的活动需求条数。</summary>
        public int? PurchaseShortfallDemandCount { get; set; }

        /// <summary>至少一个可信适用物料总采购占用超量的活动需求条数。</summary>
        public int? PurchaseExcessDemandCount { get; set; }

        /// <summary>当前采购承接证据需核实的活动需求条数。</summary>
        public int? PurchaseReviewDemandCount { get; set; }

        /// <summary>活动需求当前关联采购行去重后的条数，不累加各需求的行数。</summary>
        public int? ReceiptPurchaseLineCount { get; set; }

        /// <summary>可信整行剩余收货量为零的采购行条数。</summary>
        public int? ReceiptCompletedLineCount { get; set; }

        /// <summary>可信整行剩余收货量大于零的采购行条数。</summary>
        public int? ReceiptOutstandingLineCount { get; set; }

        /// <summary>整行收货证据需核实的采购行条数。</summary>
        public int? ReceiptReviewLineCount { get; set; }

        /// <summary>可信整行剩余收货量小于零的采购行条数。</summary>
        public int? ReceiptExcessLineCount { get; set; }

        /// <summary>关联采购行中被多个需求共用的条数。</summary>
        public int? SharedPurchaseLineCount { get; set; }

        /// <summary>有权读取维度的关注项代码；去重，不因无权隐藏维度泄露关注数量。</summary>
        public List<string> AttentionCodes { get; set; } = new List<string>();

        /// <summary>有权查看的摘要核实说明。</summary>
        public string ReviewReason { get; set; }
    }
}

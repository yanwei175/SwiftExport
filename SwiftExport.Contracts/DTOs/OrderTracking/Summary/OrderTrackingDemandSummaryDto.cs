using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>单条成品需求的下游摘要，不改变基础数量或生命周期；采购收货仍是整行事实。</summary>
    public class OrderTrackingDemandSummaryDto
    {
        /// <summary>成品需求稳定身份。</summary>
        public Guid GrossDemandId { get; set; }

        /// <summary>摘要所属来源类型，用于来源聚合及身份核对。</summary>
        public string SourceType { get; set; }

        /// <summary>摘要所属来源稳定身份。</summary>
        public Guid SourceGroupId { get; set; }

        /// <summary>基础来源身份或快照是否需核实，不依赖下游权限。</summary>
        public bool HasSourceReview { get; set; }

        /// <summary>活动需求是否缺少保存交期，不以客户端日期推算。</summary>
        public bool MissingRequiredDate { get; set; }

        /// <summary>摘要读取时的需求版本，用于识别与独立基础查询的版本差异。</summary>
        public int DemandVersion { get; set; }

        /// <summary>交付数量对应的需求单位快照，不从另一次基础读取或主数据补写。</summary>
        public string Unit { get; set; }

        /// <summary>是否为非删除活动需求；非活动需求不加入来源当前进度。</summary>
        public bool IsActive { get; set; }

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

        /// <summary>NotDelivered、Partial、Delivered、Review或Inactive；缺交期不抹掉可信交付数量。</summary>
        public string DeliveryStatus { get; set; }

        /// <summary>同需求单位的可信正式净交付数量；未知或无权为空。</summary>
        public decimal? DeliveredQty { get; set; }

        /// <summary>可信剩余未交数量；保留异常负值，不截成零。</summary>
        public decimal? RemainingQty { get; set; }

        /// <summary>按服务端业务日判断；无交期或证据异常为空。</summary>
        public bool? IsOverdue { get; set; }

        /// <summary>确定不逾期为零，未知为空。</summary>
        public int? OverdueDays { get; set; }

        /// <summary>活动物料条数，按NetDemandId去重。</summary>
        public int? ActiveMaterialCount { get; set; }

        /// <summary>当前活动物料依据需核实的条数。</summary>
        public int? MaterialReviewCount { get; set; }

        /// <summary>历史物料条数，仅提示历史，不加入活动进度。</summary>
        public int? HistoricalMaterialCount { get; set; }

        /// <summary>NotApplicable、Unallocated、Partial、Allocated、Excess、Review或Inactive；承接不代表采购确认。</summary>
        public string PurchaseStatus { get; set; }

        /// <summary>当前适用采购的物料条数，排除客供等不适用对象。</summary>
        public int? ApplicableMaterialCount { get; set; }

        /// <summary>可信承接不足物料条数。</summary>
        public int? PurchaseShortfallMaterialCount { get; set; }

        /// <summary>可信承接超量物料条数。</summary>
        public int? PurchaseExcessMaterialCount { get; set; }

        /// <summary>当前承接证据未知物料条数；有未知时不得判定全部承接。</summary>
        public int? PurchaseReviewMaterialCount { get; set; }

        /// <summary>None、Outstanding、Completed、Excess、Review或Inactive；None表示无关联采购行。</summary>
        public string ReceiptStatus { get; set; }

        /// <summary>当前关联采购行去重条数。</summary>
        public int? ReceiptPurchaseLineCount { get; set; }

        /// <summary>可信整行已收足条数。</summary>
        public int? ReceiptCompletedLineCount { get; set; }

        /// <summary>可信整行未收足条数。</summary>
        public int? ReceiptOutstandingLineCount { get; set; }

        /// <summary>整行收货未知条数。</summary>
        public int? ReceiptReviewLineCount { get; set; }

        /// <summary>可信整行超收条数。</summary>
        public int? ReceiptExcessLineCount { get; set; }

        /// <summary>多需求共用采购行条数。</summary>
        public int? SharedPurchaseLineCount { get; set; }

        /// <summary>当前有权查看的关注项代码；与筛选使用相同判定。</summary>
        public List<string> AttentionCodes { get; set; } = new List<string>();

        /// <summary>有权查看的核实说明。</summary>
        public string ReviewReason { get; set; }
    }
}

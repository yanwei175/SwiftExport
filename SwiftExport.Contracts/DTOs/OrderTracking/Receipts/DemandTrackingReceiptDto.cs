using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Receipts
{
    /// <summary>相关采购行的整行收货证据，不表示本条需求实收分配。</summary>
    public class DemandTrackingReceiptDto
    {
        /// <summary>入库单身份。</summary>
        public Guid InboundDocumentId { get; set; }
        /// <summary>入库行身份，去重依据。</summary>
        public Guid InboundLineId { get; set; }
        /// <summary>入库单号。</summary>
        public string InboundDocumentNo { get; set; }
        /// <summary>入库行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>实际供货采购行身份。</summary>
        public Guid PurchaseLineId { get; set; }
        /// <summary>采购单号。</summary>
        public string PurchaseOrderNo { get; set; }
        /// <summary>采购行号。</summary>
        public int PurchaseLineNumber { get; set; }
        /// <summary>收货产品编码快照。</summary>
        public string Sku { get; set; }
        /// <summary>收货行数量，不按关联分摊。</summary>
        public decimal Qty { get; set; }
        /// <summary>收货单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>入库过账状态。</summary>
        public InboundPostingStatusEnum PostingStatus { get; set; }
        /// <summary>实际收货日期。</summary>
        public DateTime? ReceiptDate { get; set; }
        /// <summary>过账时间。</summary>
        public DateTime? PostedDate { get; set; }
        /// <summary>反向时间。</summary>
        public DateTime? ReversedDate { get; set; }
        /// <summary>可信且有效已过账计入整行实收；历史异常为null。</summary>
        public bool? CountsTowardReceipt { get; set; }
        /// <summary>采购行是否有其他需求或物料关联。</summary>
        public bool IsSharedPurchaseLine { get; set; }
        /// <summary>本条收货证据待核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}

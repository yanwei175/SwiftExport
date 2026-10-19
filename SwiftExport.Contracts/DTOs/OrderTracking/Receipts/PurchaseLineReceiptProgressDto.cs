using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Receipts
{
    /// <summary>关联采购整行的收货进度，不是当前需求或物料的实收份额。</summary>
    public class PurchaseLineReceiptProgressDto
    {
        /// <summary>采购单身份。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>采购行身份，进度去重依据。</summary>
        public Guid PurchaseLineId { get; set; }
        /// <summary>采购单号。</summary>
        public string PurchaseOrderNo { get; set; }
        /// <summary>采购行号。</summary>
        public int PurchaseLineNumber { get; set; }
        /// <summary>采购产品编码。</summary>
        public string Sku { get; set; }
        /// <summary>采购行单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>采购整行数量，来源缺失为未知。</summary>
        public decimal? PurchaseLineQty { get; set; }
        /// <summary>可信正式净收货量，证据异常为未知。</summary>
        public decimal? ReceivedQty { get; set; }
        /// <summary>采购整行数量减正式净收货量，不截断负值。</summary>
        public decimal? RemainingQty { get; set; }
        /// <summary>采购行是否被其他物料或需求共享。</summary>
        public bool IsSharedPurchaseLine { get; set; }
        /// <summary>数量或历史证据核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}

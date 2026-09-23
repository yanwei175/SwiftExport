using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>预览中的单条库存或仅交付动作；不代表已执行。</summary>
    public class PackingListPostingActionDto
    {
        /// <summary>Delivery、Receipt、Shipment 或 Consumption。</summary>
        public string ActionKind { get; set; }
        /// <summary>物理箱单行身份。</summary>
        public Guid PackingListLineId { get; set; }
        /// <summary>交付来源身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>产品身份；非标无库存行可空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>查询图片路径，不持久化为实体列。</summary>
        public string ImagePath { get; set; }
        /// <summary>产品型号展示。</summary>
        public string Sku { get; set; }
        /// <summary>客户型号展示。</summary>
        public string CustomerSku { get; set; }
        /// <summary>来源单号。</summary>
        public string SourceNo { get; set; }
        /// <summary>动作数量单位；不得跨单位合计。</summary>
        public string Unit { get; set; }
        /// <summary>本动作正数量；方向由动作和预览操作决定。</summary>
        public decimal Quantity { get; set; }
        /// <summary>实际分配仓库，非出货地点文本。</summary>
        public Guid? WarehouseSiteId { get; set; }
        /// <summary>仓库编码展示。</summary>
        public string WarehouseCode { get; set; }
        /// <summary>仓库名称展示。</summary>
        public string WarehouseName { get; set; }
        /// <summary>新增自动入库的采购依据，非出库批次分摊。</summary>
        public Guid? PurchaseLineId { get; set; }
        /// <summary>采购单号展示。</summary>
        public string PurchaseOrderNo { get; set; }
        /// <summary>采购行号展示。</summary>
        public int? PurchaseLineNumber { get; set; }
        /// <summary>根需求计算快照身份。</summary>
        public Guid? RootNetDemandId { get; set; }
        /// <summary>客供件子需求计算快照身份。</summary>
        public Guid? ComponentNetDemandId { get; set; }
        /// <summary>根需求数量快照。</summary>
        public decimal RootQty { get; set; }
        /// <summary>子需求数量快照。</summary>
        public decimal ComponentQty { get; set; }
        /// <summary>本次来源出货数量。</summary>
        public decimal ShippedQty { get; set; }
        /// <summary>仅交付等业务说明。</summary>
        public string Explanation { get; set; }
    }
}

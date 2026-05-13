using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Warehouses
{
    /// <summary>
    /// 入库单行 DTO（对应 ReceiveLine / PurchaseReceiveLine）。
    /// </summary>
    public class ReceiveLineDto
        : BaseDto
    {
        /// <summary>关联头表单号。</summary>
        public string ReceiveNo { get; set; } = string.Empty;

        /// <summary>物料 SKU。</summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>本次入库数量。</summary>
        public decimal Qty { get; set; }

        /// <summary>单位。</summary>
        public string Unit { get; set; }

        /// <summary>关联采购单行ID。</summary>
        public int? PoLineId { get; set; }

        /// <summary>冗余采购单号。</summary>
        public string PoNo { get; set; }
    }
}

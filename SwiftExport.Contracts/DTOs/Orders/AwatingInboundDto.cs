namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 待入库候选行 DTO（按供应商 + 物料类型筛选）。
    /// </summary>
    public class AwatingInboundDto
    {
        public string PoNo { get; set; } = string.Empty;
        public int PoLineId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public decimal PoQty { get; set; }
        public decimal RecQty { get; set; }
        public decimal ReadyQty { get; set; }
        public decimal CanReceiveQty { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string MaterialType { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
    }
}

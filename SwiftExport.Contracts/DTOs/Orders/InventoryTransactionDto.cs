using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 库存交易流水 DTO。
    /// 说明：流水数据是只读历史记录，业务上仅允许新增，不允许修改。
    /// </summary>
    public class InventoryTransactionDto : BaseDto
    {
        public string WhseCode { get; set; } = string.Empty;
        public string LocationCode { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;

        public string BizType { get; set; } = string.Empty;
        public string BizNo { get; set; } = string.Empty;
        public int? BizLineId { get; set; }

        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal ChangeQty { get; set; }
        public decimal PostQty { get; set; }

        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }

        public string RefNo { get; set; }
    }
}

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购跟进更新请求 DTO。
    /// 用于更新采购行完工数量并写入跟进日志。
    /// </summary>
    public class PurchaseFollowUpUpdateDto
    {
        /// <summary>
        /// 采购订单行主键 Id。
        /// </summary>
        public int PurchaseOrderLineId { get; set; }

        /// <summary>
        /// 最新完工数量（ReadyQty）。
        /// </summary>
        public decimal ReadyQty { get; set; }

        /// <summary>
        /// 跟进备注。
        /// </summary>
        public string NoteContent { get; set; }
    }
}

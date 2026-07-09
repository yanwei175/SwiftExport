using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.PurchaseOrders.Requests
{
    /// <summary>
    /// 采购跟进日志 DTO。
    /// </summary>
    public class FollowUpLogDto : BaseDto
    {
        /// <summary>
        /// 关联的采购订单行 Uid。
        /// </summary>
        public Guid RefLineId { get; set; }

        /// <summary>
        /// 跟进备注内容。
        /// </summary>
        public string NoteContent { get; set; }
    }
}

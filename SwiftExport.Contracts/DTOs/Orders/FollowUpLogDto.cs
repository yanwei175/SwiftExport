using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购跟进日志 DTO。
    /// </summary>
    public class FollowUpLogDto : BaseDto
    {
        /// <summary>
        /// 关联的采购订单行 Id。
        /// </summary>
        public int RefLineId { get; set; }

        /// <summary>
        /// 跟进备注内容。
        /// </summary>
        public string NoteContent { get; set; }
    }
}

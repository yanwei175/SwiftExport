using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 获取最新单号请求。
    /// </summary>
    public class DocumentNoRequestDto : BaseDto
    {
        /// <summary>
        /// DocumentTypeStaticsEnum 单据类型（SO/PO/MO...）。
        /// </summary>
        public string DocType { get; set; }

        /// <summary>
        /// 业务日期（为空时使用当前系统时间）。
        /// 用于计算 YearMonth 分段。
        /// </summary>
        public DateTime? BusinessDate { get; set; }

        /// <summary>
        /// 流水号位数（默认 4 位）。
        /// </summary>
        public int SerialWidth { get; set; } = 4;

        /// <summary>
        /// 操作人（可选）。
        /// </summary>
        public string Operator { get; set; }
    }
}

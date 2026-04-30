using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems
{
    /// <summary>
    /// 单据流水号实体。
    /// 对应表：DocumentSequence。
    /// </summary>
    [Table("DocumentSequence")]
    public class DocumentSequence : BaseEntity
    {
        /// <summary>
        /// 单据类型（例如 SO/PO/MO）。
        /// </summary>
        public string DocType { get; set; } = string.Empty;

        /// <summary>
        /// 年月分段（yyyyMM）。
        /// </summary>
        public string YearMonth { get; set; } = string.Empty;

        /// <summary>
        /// 当前已使用流水号。
        /// </summary>
        public int CurrentNo { get; set; }
    }
}

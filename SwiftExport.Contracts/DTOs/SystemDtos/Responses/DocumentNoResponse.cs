using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos.Responses
{
    /// <summary>
    /// 单号生成结果。
    /// </summary>
    public class DocumentNoResponse : BaseDto
    {
        /// <summary>
        /// 单据类型。
        /// </summary>
        public string DocType { get; set; }

        /// <summary>
        /// 年月分段（yyyyMM）。
        /// </summary>
        public string YearMonth { get; set; }

        /// <summary>
        /// 本次分配到的流水号。
        /// </summary>
        public int CurrentNo { get; set; }

        /// <summary>
        /// 完整单号。
        /// 规则：{DocType}{YearMonth}{流水号补零}。
        /// </summary>
        public string DocumentNo { get; set; }
    }
}

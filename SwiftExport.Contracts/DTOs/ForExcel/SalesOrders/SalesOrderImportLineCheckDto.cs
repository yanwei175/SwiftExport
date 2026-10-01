using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>逐行匹配结果，标准行唯一有效匹配才返回可用产品身份。</summary>
    public class SalesOrderImportLineCheckDto
    {
        /// <summary>对应请求行标识。</summary>
        public Guid ClientLineId { get; set; }
        /// <summary>Matched、NonStandard、NotMatched、Ambiguous 或 Unavailable。</summary>
        public string MatchStatus { get; set; }
        /// <summary>唯一有效匹配时返回；非标或失败时为空。</summary>
        public SalesOrderImportProductCandidateDto Product { get; set; }
        /// <summary>全部同型号候选，禁止静默取第一条。</summary>
        public List<SalesOrderImportProductCandidateDto> Candidates { get; set; } = new List<SalesOrderImportProductCandidateDto>();
        /// <summary>该行的错误及待确认提示。</summary>
        public List<SalesOrderImportIssueDto> Issues { get; set; } = new List<SalesOrderImportIssueDto>();
    }
}

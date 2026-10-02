using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>逐行匹配结果；不作为后续保存授权凭据。</summary>
    public sealed class PurchaseOrderImportLineCheckDto
    {
        /// <summary>对应输入行标识。</summary>
        public Guid ClientLineId { get; set; }
        /// <summary>Matched、NonStandard、NotMatched、Ambiguous 或 Unavailable。</summary>
        public string MatchStatus { get; set; }
        /// <summary>成功定位的标准产品，其他情况为空。</summary>
        public PurchaseOrderImportProductCandidateDto Product { get; set; }
        /// <summary>当前客户型号下全部候选。</summary>
        public List<PurchaseOrderImportProductCandidateDto> Candidates { get; set; } = new List<PurchaseOrderImportProductCandidateDto>();
        /// <summary>行问题。</summary>
        public List<PurchaseOrderImportIssueDto> Issues { get; set; } = new List<PurchaseOrderImportIssueDto>();
    }
}


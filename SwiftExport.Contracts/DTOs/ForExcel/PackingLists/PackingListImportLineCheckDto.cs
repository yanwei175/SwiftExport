using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>PackingList 导入结果；只传递业务值，不持有界面状态。</summary>
    public sealed class PackingListImportLineCheckDto
    {
        public Guid ClientLineId { get; set; }
        public string MatchStatus { get; set; }
        public PackingListImportProductCandidateDto Product { get; set; }
        public List<PackingListImportProductCandidateDto> Candidates { get; set; } = new List<PackingListImportProductCandidateDto>();
        public List<PackingListImportIssueDto> Issues { get; set; } = new List<PackingListImportIssueDto>();
    }
}

using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>PackingList 导入结果；只传递业务值，不持有界面状态。</summary>
    public sealed class PackingListImportProductCandidateDto
    {
        public Guid CustomerProductId { get; set; }
        public Guid? ProductId { get; set; }
        public string CustomerSku { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string Unit { get; set; }
        public string MainImage { get; set; }
        public bool IsUsable { get; set; }
    }
}

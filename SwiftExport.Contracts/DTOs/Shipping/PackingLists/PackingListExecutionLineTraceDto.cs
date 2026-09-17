using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>物理箱单行的实际库存流水和原计算依据，不是重新分配结果。</summary>
    public class PackingListExecutionLineTraceDto : BaseDto
    {
        public Guid ExecutionId { get; set; }
        public int LineNumber { get; set; }
        public Guid PackingListLineId { get; set; }
        public Guid GrossDemandId { get; set; }
        public string ActionKind { get; set; }
        public Guid? ProductId { get; set; }
        public string Unit { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public Guid? PurchaseLineId { get; set; }
        public Guid? RootNetDemandId { get; set; }
        public Guid? ComponentNetDemandId { get; set; }
        public decimal RootQtySnapshot { get; set; }
        public decimal ComponentQtySnapshot { get; set; }
        public decimal ShippedQtySnapshot { get; set; }
        public decimal Qty { get; set; }
        public Guid? DocumentId { get; set; }
        public string DocumentNo { get; set; }
        public int? DocumentVersion { get; set; }
        public Guid? DocumentLineId { get; set; }
        public Guid? LedgerId { get; set; }
        public Guid? OriginalExecutionLineId { get; set; }
    }
}

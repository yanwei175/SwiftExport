using System;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query
{
    /// <summary>入库明细详情数据；不包含勾选与编辑状态，不作为保存命令。</summary>
    public class InboundLineDetailDto : BaseDto
    {
        public Guid InboundDocumentId { get; set; }
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        public string SkuSnapshot { get; set; } = string.Empty;
        public string DescriptionSnapshot { get; set; } = string.Empty;
        public string UnitSnapshot { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public Guid SourceLineId { get; set; }
        /// <summary>查询时的采购行版本；不持久化到入库行，不等同入库行 Version 或可收额度锁。</summary>
        public int SourceVersion { get; set; }
        public Guid? OriginalLineId { get; set; }
        public string SourceNo { get; set; } = string.Empty;
        public string BoNo { get; set; } = string.Empty;
        public string LinkSku { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public decimal OriginalQty { get; set; }
        public decimal RemainingReceivableQty { get; set; }
        /// <summary>同来源其他活动草稿数，仅提示，不预占。</summary>
        public int OtherDraftCount { get; set; }
        /// <summary>其他活动草稿录入量合计，不扣减剩余可收量。</summary>
        public decimal OtherDraftQuantity { get; set; }
    }
}

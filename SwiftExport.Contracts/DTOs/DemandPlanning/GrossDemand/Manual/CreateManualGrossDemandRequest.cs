using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>携带新建手工成品需求的产品、数量和交付日期输入。</summary>
    public class CreateManualGrossDemandRequest
    {
        /// <summary>一次创建意图的稳定标识；结果未知时原样重试。</summary>
        public Guid CommandId { get; set; }
        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public string Remark { get; set; }
    }
}

using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>携带手工来源需求身份、预期版本和替换后的业务输入。</summary>
    public class UpdateManualGrossDemandRequest
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
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

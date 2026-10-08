using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>返回流量测试组分页摘要，不混入功能行或写入字段。</summary>
    public sealed class FlowListItemDto : BaseDto
    {
        public string SkuSnapshot { get; set; }
        public Guid MaterialId { get; set; }
        public DateTime TestDate { get; set; }
        public string MarketDescription { get; set; }
        public string ConfigurationDescription { get; set; }
        public string Remark { get; set; }
    }
}


using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>返回一组流量测试的历史头信息及全部未删除功能行。</summary>
    public sealed class FlowDetailDto : BaseDto
    {
        public string SkuSnapshot { get; set; }
        public Guid MaterialId { get; set; }
        public DateTime TestDate { get; set; }
        public string MarketDescription { get; set; }
        public string ConfigurationDescription { get; set; }
        public string Remark { get; set; }
        public List<FlowLineDto> Lines { get; set; } = new List<FlowLineDto>();
    }
}


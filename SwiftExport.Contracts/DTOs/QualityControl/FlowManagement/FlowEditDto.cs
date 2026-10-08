using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>提交完整测试组的可编辑字段；快照、审计及头身份由服务端维护。</summary>
    public sealed class FlowEditDto
    {
        public Guid MaterialId { get; set; }
        public DateTime TestDate { get; set; }
        public string MarketDescription { get; set; }
        public string ConfigurationDescription { get; set; }
        public string Remark { get; set; }
        /// <summary>最终保留的全部功能行；更新时遗漏的旧行将被软删除。</summary>
        public List<FlowLineEditDto> Lines { get; set; } = new List<FlowLineEditDto>();
    }
}


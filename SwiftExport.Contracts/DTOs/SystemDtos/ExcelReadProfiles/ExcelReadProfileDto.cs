using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>主配置、完整字段快照及配置结构完整性；CanRead 不代表实际工作簿已经验证。</summary>
    public sealed class ExcelReadProfileDto : BaseDto
    {
        /// <summary>稳定单据分组。</summary>
        public string GroupCode { get; set; }
        /// <summary>同分组内唯一的配置名称。</summary>
        public string ConfigName { get; set; }
        /// <summary>结束方式：0 未指定、1 判断列末行、2 汇总行之前。</summary>
        public int EndMode { get; set; }
        /// <summary>结束方式为 1 时的列字母，如 A。</summary>
        public string LastRowColumn { get; set; }
        /// <summary>结束方式为 2 时的汇总单元格，如 D30。</summary>
        public string SummaryCellAddress { get; set; }
        /// <summary>配置说明。</summary>
        public string Remark { get; set; }
        /// <summary>全部未删除字段。</summary>
        public List<ExcelReadFieldDto> Fields { get; set; } = new List<ExcelReadFieldDto>();
        /// <summary>配置结构是否足以发起读取；客户端仍须检查字段目录、工作表和实际数据。</summary>
        public bool CanRead { get; set; }
        /// <summary>结构完整性缺项。</summary>
        public List<ExcelReadProfileIssueDto> Issues { get; set; } = new List<ExcelReadProfileIssueDto>();
    }
}


using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>提交主配置及全部字段的完整快照；缺席字段表示清除，必须提供 Fields。</summary>
    public sealed class UpdateExcelReadProfileRequest
    {
        /// <summary>主配置的预期版本，任何字段修改也递增主版本。</summary>
        public int ExpectedVersion { get; set; }

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

        /// <summary>完整字段列表；空列表明确清除全部字段，null 拒绝保存。</summary>
        public List<ExcelReadFieldDto> Fields { get; set; }
    }
}


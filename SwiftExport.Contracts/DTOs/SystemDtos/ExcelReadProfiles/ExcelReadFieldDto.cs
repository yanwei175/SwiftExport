namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>一个字段的地址配置；查询与更新均表达同一稳定映射，不携带控件或选择状态。</summary>
    public sealed class ExcelReadFieldDto
    {
        /// <summary>稳定目标字段路径。</summary>
        public string TargetFieldKey { get; set; }

        /// <summary>可选的工作表名称，仅供参考；前端读取使用活动工作表。</summary>
        public string WorksheetName { get; set; }

        /// <summary>单个 A1 地址。</summary>
        public string CellAddress { get; set; }

        /// <summary>是否从此位置逐行向下读取。</summary>
        public bool IsDownward { get; set; }
    }
}


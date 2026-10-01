namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>配置尚不可读取的结构化原因；不检查实际工作簿或业务字段值。</summary>
    public sealed class ExcelReadProfileIssueDto
    {
        /// <summary>稳定缺项代码。</summary>
        public string Code { get; set; }

        /// <summary>对应配置字段路径。</summary>
        public string Field { get; set; }

        /// <summary>中文说明。</summary>
        public string Message { get; set; }
    }
}


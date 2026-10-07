namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>定义地址读取配置名称冲突及正常配置响应中的完整性问题，不将问题集合作为 HTTP 失败。</summary>
    public static class ExcelReadProfileErrorCodes
    {
        /// <summary>同一单据分组的配置名称触发唯一约束冲突。</summary>
        public const string ExcelReadDuplicateName = "EXCEL_READ_DUPLICATE_NAME";
        /// <summary>配置尚未指定字段读取位置。</summary>
        public const string ExcelReadFieldsRequired = "EXCEL_READ_FIELDS_REQUIRED";
        /// <summary>向下读取字段的起始行不一致。</summary>
        public const string ExcelReadStartRowMismatch = "EXCEL_READ_START_ROW_MISMATCH";
        /// <summary>向下读取缺少结束判断规则。</summary>
        public const string ExcelReadEndRequired = "EXCEL_READ_END_REQUIRED";
        /// <summary>配置缺少末行判断列。</summary>
        public const string ExcelReadColumnRequired = "EXCEL_READ_COLUMN_REQUIRED";
        /// <summary>配置缺少有效汇总单元格。</summary>
        public const string ExcelReadSummaryRequired = "EXCEL_READ_SUMMARY_REQUIRED";
        /// <summary>汇总行未位于明细起始行之后。</summary>
        public const string ExcelReadSummaryBeforeStart = "EXCEL_READ_SUMMARY_BEFORE_START";
    }
}

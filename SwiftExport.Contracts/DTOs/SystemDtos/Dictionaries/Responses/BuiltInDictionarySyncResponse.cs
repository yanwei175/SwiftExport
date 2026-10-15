namespace SwiftExport.Contracts.DTOs.SystemDtos.Responses
{
    /// <summary>显式同步内置字典的结果；仅统计本次新增记录及有效标记修复，不包含用户自定义项。</summary>
    public class BuiltInDictionarySyncResponse
    {
        /// <summary>本次补齐的内置字典类型数量。</summary>
        public int AddedTypeCount { get; set; }

        /// <summary>本次恢复删除标记或系统标记的内置类型数量。</summary>
        public int UpdatedTypeCount { get; set; }

        /// <summary>本次补齐的内置字典默认值数量。</summary>
        public int AddedDataCount { get; set; }

        /// <summary>本次恢复启用、删除或系统标记的内置默认值数量。</summary>
        public int UpdatedDataCount { get; set; }
    }
}

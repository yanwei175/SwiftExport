namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>按预期版本修改表头、目标字段和启用状态；分组不允许跨组更改。</summary>
    public sealed class UpdateExcelFieldMappingRequest
    {
        public int ExpectedVersion { get; set; }
        public string ExcelHeader { get; set; }
        public string TargetFieldKey { get; set; }
        public bool IsEnabled { get; set; }
        public string Remark { get; set; }
    }
}

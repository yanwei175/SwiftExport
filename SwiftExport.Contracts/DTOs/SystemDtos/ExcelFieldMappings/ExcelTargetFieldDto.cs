namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>可映射的目标字段及其创建、更新适用性；字段目录由服务端代码维护。</summary>
    public sealed class ExcelTargetFieldDto
    {
        public string FieldKey { get; set; }
        public string DisplayName { get; set; }
        public bool SupportsCreate { get; set; }
        public bool SupportsUpdate { get; set; }
    }
}

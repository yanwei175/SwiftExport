namespace SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters
{
    /// <summary>返回已注册参数的说明、当前值及并发版本；未配置参数仍可展示。</summary>
    public class SystemParameterDto
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int Version { get; set; }
        public bool IsConfigured { get; set; }
    }
}

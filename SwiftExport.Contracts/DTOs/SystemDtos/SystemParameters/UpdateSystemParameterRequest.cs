namespace SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters
{
    /// <summary>按读取版本保存参数值；版本 0 表示首次配置，不允许覆盖并发创建的记录。</summary>
    public class UpdateSystemParameterRequest
    {
        public string Value { get; set; } = string.Empty;
        public int ExpectedVersion { get; set; }
    }
}

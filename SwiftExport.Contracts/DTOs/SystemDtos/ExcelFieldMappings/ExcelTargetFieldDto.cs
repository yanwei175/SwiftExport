// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>可映射目标的展示描述；当前 VSTO 根据已注册模型的属性标记生成，不再由后端目录接口提供。</summary>
    public sealed class ExcelTargetFieldDto
    {
        public string FieldKey { get; set; }
        public string DisplayName { get; set; }
        public bool SupportsCreate { get; set; }
        public bool SupportsUpdate { get; set; }
    }
}

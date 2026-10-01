// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>供导入配置页面选择的稳定业务分组。</summary>
    public sealed class ExcelFieldGroupDto
    {
        public string GroupCode { get; set; }
        public string DisplayName { get; set; }
    }
}

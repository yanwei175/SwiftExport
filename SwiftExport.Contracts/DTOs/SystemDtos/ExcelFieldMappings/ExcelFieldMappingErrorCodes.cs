// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>字段映射配置的稳定业务错误代码。</summary>
    public static class ExcelFieldMappingErrorCodes
    {
        /// <summary>同组规范化表头已被其他活动映射使用。</summary>
        public const string DuplicateHeader = "EXCEL_FIELD_MAPPING_DUPLICATE_HEADER";
    }
}

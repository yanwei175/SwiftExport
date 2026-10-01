// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>返回一条已保存的 Excel 表头到导入字段的映射。</summary>
    public sealed class ExcelFieldMappingDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string GroupCode { get; set; }
        public string ExcelHeader { get; set; }
        public string TargetFieldKey { get; set; }
        public bool IsEnabled { get; set; }
        public string Remark { get; set; }
    }
}

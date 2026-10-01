// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>一次提交一个分组的映射快照；缺席行不表示删除。</summary>
    public sealed class SaveExcelFieldMappingGroupRequest
    {
        /// <summary>包含已保存行及本地新增行，新增行的 Uid 保持空值。</summary>
        public List<SaveExcelFieldMappingRowRequest> Rows { get; set; } = new List<SaveExcelFieldMappingRowRequest>();
    }
}

using System;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>按单据分组供配置选择器读取的共享配置摘要。</summary>
    public sealed class ExcelReadProfileListItemDto
    {
        /// <summary>配置主键。</summary>
        public Guid Uid { get; set; }

        /// <summary>主配置版本。</summary>
        public int Version { get; set; }

        /// <summary>单据分组。</summary>
        public string GroupCode { get; set; }

        /// <summary>配置名称。</summary>
        public string ConfigName { get; set; }
    }
}


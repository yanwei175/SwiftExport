namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles
{
    /// <summary>创建空的共享配置；后续在单据页面逐个配置字段。</summary>
    public sealed class CreateExcelReadProfileRequest
    {
        /// <summary>单据分组标识，创建后不可修改。</summary>
        public string GroupCode { get; set; }

        /// <summary>配置名称。</summary>
        public string ConfigName { get; set; }

        /// <summary>配置说明。</summary>
        public string Remark { get; set; }
    }
}


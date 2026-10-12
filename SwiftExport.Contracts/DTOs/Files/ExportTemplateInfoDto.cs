namespace SwiftExport.Contracts.DTOs.Files
{
    /// <summary>描述服务器当前启用的 Excel 模板、数据契约和文件完整性；不暴露物理存储路径。</summary>
    public class ExportTemplateInfoDto
    {
        /// <summary>稳定模板标识，由小写字母、数字和短横线组成。</summary>
        public string TemplateId { get; set; }

        /// <summary>供业务页面展示和选择的模板名称。</summary>
        public string DisplayName { get; set; }

        /// <summary>模板所属单据类型，标准类型沿用现有单据类型常量。</summary>
        public string DocumentType { get; set; }

        /// <summary>模板要求的命名变量和数据结构标识。</summary>
        public string DataContractKey { get; set; }

        /// <summary>命名变量和数据结构的正整数版本。</summary>
        public int DataContractVersion { get; set; }

        /// <summary>当前发布的模板文件正整数版本，与数据契约版本分别管理。</summary>
        public int TemplateVersion { get; set; }

        /// <summary>模板文件实际字节数，用于下载和缓存完整性检查。</summary>
        public long FileLength { get; set; }

        /// <summary>模板内容的 SHA-256 十六进制校验值。</summary>
        public string Sha256 { get; set; }
    }
}

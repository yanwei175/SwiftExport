namespace SwiftExport.Contracts.DTOs.Files
{
    /// <summary>描述一次文件上传成功后可持久保存的逻辑引用和受保护读取地址。</summary>
    public class FileUploadResultDto
    {
        /// <summary>获取或设置配置中的稳定容器标识。</summary>
        public string ContainerId { get; set; }

        /// <summary>获取或设置容器内使用正斜线分隔的逻辑相对路径。</summary>
        public string Path { get; set; }

        /// <summary>获取或设置由容器标识和逻辑路径组成的稳定引用。</summary>
        public string Reference { get; set; }

        /// <summary>获取或设置需要 Bearer 身份访问的完整读取地址。</summary>
        public string ReadUrl { get; set; }
    }
}

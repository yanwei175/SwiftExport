namespace SwiftExport.Contracts.DTOs.Files.ProductImages
{
    /// <summary>描述产品图片批量上传中一个输入文件的独立结果。</summary>
    public class ProductImageUploadItemResultDto
    {
        /// <summary>获取或设置从零开始的输入顺序。</summary>
        public int Index { get; set; }

        /// <summary>获取或设置客户端提供的原始文件名。</summary>
        public string InputFileName { get; set; }

        /// <summary>获取或设置通过校验后的 JPEG 目标文件名；名称非法时为空。</summary>
        public string TargetFileName { get; set; }

        /// <summary>获取或设置该项目是否完成原图、缩略图和完成记录发布。</summary>
        public bool Success { get; set; }

        /// <summary>获取或设置失败时的稳定错误代码。</summary>
        public string ErrorCode { get; set; }

        /// <summary>获取或设置不包含物理路径和技术异常的简短结果说明。</summary>
        public string Message { get; set; }

        /// <summary>获取或设置成功原图的公开完整地址。</summary>
        public string OriginalUrl { get; set; }

        /// <summary>获取或设置成功缩略图的公开完整地址。</summary>
        public string ThumbnailUrl { get; set; }

        /// <summary>获取或设置最终原图 JPEG 字节的 SHA-256 十六进制指纹。</summary>
        public string OriginalSha256 { get; set; }

        /// <summary>获取或设置最终缩略图 JPEG 字节的 SHA-256 十六进制指纹。</summary>
        public string ThumbnailSha256 { get; set; }
    }
}

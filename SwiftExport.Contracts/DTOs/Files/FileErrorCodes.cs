namespace SwiftExport.Contracts.DTOs.Files
{
    /// <summary>定义通用文件传输 API 的稳定错误代码。</summary>
    public static class FileErrorCodes
    {
        /// <summary>默认拒绝覆盖时目标文件已经存在。</summary>
        public const string Conflict = "FILE_CONFLICT";

        /// <summary>上传内容超过容器配置的字节上限。</summary>
        public const string UploadTooLarge = "FILE_UPLOAD_TOO_LARGE";

        /// <summary>上传请求未使用原始二进制媒体类型。</summary>
        public const string UnsupportedMediaType = "FILE_UNSUPPORTED_MEDIA_TYPE";
    }
}

namespace SwiftExport.Contracts.DTOs.Files.ProductImages
{
    /// <summary>定义产品图片批量上传逐项返回的稳定错误代码。</summary>
    public static class ProductImageErrorCodes
    {
        /// <summary>文件名不能安全映射为产品图片目标。</summary>
        public const string InvalidFileName = "PRODUCT_IMAGE_INVALID_FILE_NAME";

        /// <summary>文件扩展名或实际编码不是受支持的 JPEG、PNG、WebP。</summary>
        public const string UnsupportedType = "PRODUCT_IMAGE_UNSUPPORTED_TYPE";

        /// <summary>文件扩展名与实际图片编码不一致。</summary>
        public const string TypeMismatch = "PRODUCT_IMAGE_TYPE_MISMATCH";

        /// <summary>动画或多帧图片不在首期支持范围。</summary>
        public const string MultipleFrames = "PRODUCT_IMAGE_MULTIPLE_FRAMES";

        /// <summary>单个上传文件超过配置上限。</summary>
        public const string FileTooLarge = "PRODUCT_IMAGE_FILE_TOO_LARGE";

        /// <summary>图片像素尺寸超过配置上限。</summary>
        public const string PixelLimitExceeded = "PRODUCT_IMAGE_PIXEL_LIMIT_EXCEEDED";

        /// <summary>同一批次已有项目映射到相同目标文件。</summary>
        public const string DuplicateTargetInBatch = "DUPLICATE_TARGET_IN_BATCH";

        /// <summary>图片内容损坏或无法完成安全转码。</summary>
        public const string ProcessingFailed = "PRODUCT_IMAGE_PROCESSING_FAILED";

        /// <summary>当前进程图像处理容量已满，本项目未进入无界等待队列。</summary>
        public const string ProcessingBusy = "PRODUCT_IMAGE_PROCESSING_BUSY";

        /// <summary>原图、缩略图或配对记录未能完整发布。</summary>
        public const string PublishFailed = "PRODUCT_IMAGE_PUBLISH_FAILED";
    }
}

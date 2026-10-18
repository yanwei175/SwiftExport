namespace SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters
{
    /// <summary>共享系统参数的稳定标识；参数定义和校验由应用层维护。</summary>
    public static class SystemParameterKeys
    {
        /// <summary>新建销售订单未指定交期时，在订单日期上增加的自然日天数；系统初始值为六十天。</summary>
        public const string SalesDefaultDeliveryDays = "Sales.DefaultDeliveryDays";
        /// <summary>重建产品图片引用时使用的 HTTP/HTTPS 基址或 Windows 本地、UNC 目录。</summary>
        public const string ProductImagesBaseUrl = "Product.Images.BaseUrl";
        /// <summary>上传回执及网络缩略图使用的公开 HTTP/HTTPS 原图目录。</summary>
        public const string ProductImagesUploadBaseUrl = "Product.Images.UploadBaseUrl";
    }
}

namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>由 WebAPI 组合根提供启用状态和已验证的产品图片存储快照。</summary>
public interface IProductImageStorageProvider
{
    /// <summary>上传启用时返回存储快照；未启用时返回 false 且不创建目录。</summary>
    bool TryGetEnabledStorage(out ProductImageStorageContext? context);
}

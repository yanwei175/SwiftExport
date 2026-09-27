using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>负责单张本地图片的流生命周期和上传结果，不持有页面状态。</summary>
public interface IProductImageUploadClient
{
    /// <summary>调用批量端点上传一张图片；不自动重试覆盖操作。</summary>
    Task<ServiceResult<ProductImageBatchUploadResultDto>> UploadAsync(string path);
}

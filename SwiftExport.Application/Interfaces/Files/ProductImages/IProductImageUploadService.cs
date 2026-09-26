using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>编排已授权产品图片批次的命名、处理、同名互斥及三份非原子发布。</summary>
public interface IProductImageUploadService
{
    /// <summary>在读取 multipart 主体或创建目录前验证实时 Admin 并建立批次。</summary>
    Task<ServiceResult<ProductImageBatchSession>> BeginBatchAsync(CancellationToken cancellationToken = default);

    /// <summary>顺序消费一个文件节，安全排空超限或无效项目，并把结果加入批次。</summary>
    Task<ProductImageUploadItemResultDto> UploadItemAsync(
        ProductImageBatchSession session,
        int index,
        string inputFileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>按输入顺序汇总逐项结果；整体成功不代表每张图片成功。</summary>
    ProductImageBatchUploadResultDto CompleteBatch(ProductImageBatchSession session);
}

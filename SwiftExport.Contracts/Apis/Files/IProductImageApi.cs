using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Files
{
    /// <summary>上传产品图片，由服务端覆盖同名原图并生成缩略图；基址为产品图片控制器。</summary>
    public interface IProductImageApi
    {
        /// <summary>提交名为 files 的文件流，返回逐项发布结果；调用方负责释放输入流。</summary>
        [Multipart]
        [Post("/batch")]
        Task<ServiceResult<ProductImageBatchUploadResultDto>> UploadAsync(
            [AliasAs("files")] IEnumerable<StreamPart> files,
            CancellationToken cancellationToken = default);
    }
}

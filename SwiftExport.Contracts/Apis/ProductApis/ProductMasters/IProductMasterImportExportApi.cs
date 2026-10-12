using Refit;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>父级型号只读预览、管理员提交及筛选导出，工作簿在客户端处理。</summary>
    public interface IProductMasterImportExportApi
    {
        /// <summary>只读校验整个批次。</summary>
        [Post("/preview")]
        Task<ServiceResult<ProductMasterImportPreviewResult>> PreviewAsync([Body] ProductMasterImportPreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>管理员原子提交全批。</summary>
        [Post("/import")]
        Task<ServiceResult<ProductMasterImportResult>> ImportAsync([Body] ProductMasterImportRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取最多一万行完整筛选结果。</summary>
        [Post("/export")]
        Task<ServiceResult<List<ProductMasterExportRowDto>>> ExportAsync([Body] ProductMasterExportRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}

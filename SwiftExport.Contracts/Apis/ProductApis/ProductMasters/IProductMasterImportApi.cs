using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>每次提交最多一百行父级资料；不提供服务器预览或工作簿上传。</summary>
    public interface IProductMasterImportApi
    {
        /// <summary>活动管理员按行保存，成功和失败均按原文件行号返回。</summary>
        [Post("/import")]
        Task<ServiceResult<BatchSaveResponseBase>> ImportAsync([Body] List<ProductMasterImportRowDto> rows, CancellationToken cancellationToken = default);
    }
}

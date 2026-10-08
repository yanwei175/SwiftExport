using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>产品 BOM 专项绑定 API，使用产品 Controller 基地址，避免普通产品整包更新。</summary>
    public interface IProductBomBindingApi
    {
        /// <summary>按版本只保存产品 BOM 绑定；首次组成仍通过 BOM 批量接口独立保存。</summary>
        [Post("/{id}/bom-binding")]
        Task<ServiceResult<SaveItemResult>> BindAsync(Guid id, [Body] ProductBomBindingRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}

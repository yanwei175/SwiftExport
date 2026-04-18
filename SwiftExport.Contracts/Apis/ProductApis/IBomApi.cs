using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IBomApi : IGenericApi<BomDto>
    {
        [Get("/tree/{productSku}")]
        Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku);

        [Get("/validate/{productSku}")]
        Task<ServiceResult<bool>> ValidateBomAsync(string productSku);
    }
}
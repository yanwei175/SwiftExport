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
        Task<ServiceResult<List<BomTreeDto>>> GetBomTreeAsync(string productSku);
        [Get("/by-no/{bomNo}")]
        Task<ServiceResult<List<BomDto>>> GetBomByBomNo(string bomNo);

        [Get("/validate/{productSku}")]
        Task<ServiceResult<bool>> ValidateBomAsync(string productSku);
    }
}
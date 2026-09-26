using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.SupplierApis
{
    public interface ISupplierApi : IGenericApi<SupplierDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync([Body] SupplierSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync([Body] SupplierBatchSaveRequest request);
    }
}

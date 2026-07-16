using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SupplierApis
{
    public interface ISupplierProductApi : IGenericApi<SupplierProductDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync([Body] SupplierProductSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync([Body] SupplierProductBatchSaveRequest request);

        [Post("/set-product-default-supplier")]
        Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(
            [Body] SetProductDefaultSupplierCommand command);
    }
}

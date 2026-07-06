using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

using SwiftExport.Contracts.DTOs.Suppliers.Requests;
namespace SwiftExport.Contracts.Apis.SupplierApis
{
    public interface ISupplierApi : IGenericApi<SupplierDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPagedAsync([Query] SupplierSearchRequest request);
    }
}

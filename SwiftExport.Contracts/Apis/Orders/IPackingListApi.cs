using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    public interface IPackingListApi : IGenericApi<PackingListDto>
    {
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<PackingListDto>>> GetPagedListAsync([Body] PackingListSearchDto search);

        [Get("/{id}/detail")]
        Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(int id);

        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] PackingListDetailDto request);

        [Post("/post")]
        Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([Body] PackingListPostRequestDto request);
    }
}

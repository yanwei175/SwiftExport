using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    public interface IPackingListApi : IGenericApi<PackingListHeadDto>
    {
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetHeadPagedListAsync([Body] PackingListSearchDto search);

        [Get("/{id}/detail")]
        Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(int id);
        [Get("/{id}/lines")]
        Task<ServiceResult<List<PackingListLineDto>>> GetLinesByHeadIdAsync(int id);

        [Get("/unshipped/search")]
        Task<ServiceResult<PagedResultDto<PackingListLineDto>>> GetUnshippedForPagedListAsync([Body] PackingListSearchDto search);



        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] PackingListDetailDto request);

        [Post("/post")]
        Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([Body] PackingListPostRequestDto request);
    }
}

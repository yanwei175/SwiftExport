using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    public interface IPackingListApi : IGenericApi<PackingListHeadDto>
    {

        [Post("/search")]
        Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetHeadPagedListAsync([Body] PackingListSearchDto search);

        [Get("/{uid}/detail")]
        Task<ServiceResult<PackingListDetailDto>> GetDetailByUidAsync(Guid uid);
        [Get("/{uid}/lines")]
        Task<ServiceResult<List<PackingListLineDto>>> GetLinesByHeadUidAsync(Guid uid);

        [Get("/unshipped/search")]
        Task<ServiceResult<PagedResultDto<PackingListLineDto>>> GetUnshippedForPagedListAsync([Body] PackingListSearchDto search);



        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] PackingListDetailDto request);

        [Post("/post")]
        Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([Body] PackingListPostRequestDto request);
    }
}

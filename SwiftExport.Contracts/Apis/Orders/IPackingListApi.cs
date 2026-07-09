using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.PackingLists.Requests;
using SwiftExport.Contracts.DTOs.PackingLists.Responses;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    public interface IPackingListApi : IGenericApi<PackingListHeadDto>
    {

        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<PackingListHeadDto>>> GetHeadPagedListAsync([Body] PackingListSearchRequest search);

        [Get("/{uid}/detail")]
        Task<ServiceResult<PackingListDetailDto>> GetDetailByUidAsync(Guid uid);
        [Get("/{uid}/lines")]
        Task<ServiceResult<List<PackingListLineDto>>> GetLinesByHeadUidAsync(Guid uid);

        [Get("/unshipped/search")]
        Task<ServiceResult<PagedResponseBase<PackingListLineDto>>> GetUnshippedForPagedListAsync([Body] PackingListSearchRequest search);



        [Post("/detail/save")]
        Task<ServiceResult<int>> SaveDetailAsync([Body] PackingListDetailDto request);

        [Post("/post")]
        Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([Body] PackingListPostRequestDto request);
    }
}

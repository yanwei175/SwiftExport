using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Common;

using SwiftExport.Contracts.DTOs.Products.Requests;
namespace SwiftExport.Application.Interfaces
{
    public partial interface IBomService
    {
    Task<ServiceResult<BomDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<BomDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<BomDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(BomDto dto);
    Task<ServiceResult<int>> UpdateAsync(BomDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BomBatchSaveResponse>> BatchSaveAsync(BomBatchSaveRequest batchReq);



        /// <summary>
        /// 根据毛需求集合拆解为净需求集合（核心拆解入口）。
        /// </summary>
        /// <param name="request">毛需求集合</param>
        /// <returns>拆解后的依赖需求集合</returns>
        Task<List<DependentDemand>> 根据毛需求集合拆解成净需求(List<IndependentDemand> request);

        Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo);

        //以下为批量操作

        Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNoAsync(string bomNo);


        // 校验 BOM 合理性
        Task<ServiceResult<bool>> ValidateBomAsync(string productSku);

        // 获取 BOM 树
        Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku);

        //同步批量的BomItems
        //Task<ServiceResult<int>> SyncBomItemsAsync(IEnumerable<Bom> items);
    }
}

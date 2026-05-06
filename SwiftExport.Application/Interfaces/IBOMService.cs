using SwiftExport.Contracts.DTOs.Orders.DependentDemands;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces
{
    public interface IBomService : IBaseService<Bom, BomDto>
    {
        /// <summary>
        /// 计算BOM拆解结果（纯计算，不做最终落库提交）。
        /// </summary>
        /// <param name="request">拆解请求对象</param>
        /// <returns>BOM拆解后的实体对象列表</returns>
        Task<List<DependentDemand>> CalculateDecomposeAsync(DecomposeRequestDto request);

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

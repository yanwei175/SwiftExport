using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces
{
    public interface IBomService : IBaseService<Bom,BomDto>
    {
        Task<ServiceResult<int>> DeleteBomByBomNoAsync(string bomNo);

        //以下为批量操作

        Task<ServiceResult<List<BomDto>>> GetBomByBomNoAsync(string bomNo);


        // 校验 BOM 合理性
        Task<ServiceResult<bool>> ValidateBomAsync(string productSku);

        // 获取 BOM 树
        Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTreeAsync(string productSku);

        //同步批量的BomItems
        //Task<ServiceResult<int>> SyncBomItemsAsync(IEnumerable<Bom> items);
    }
}

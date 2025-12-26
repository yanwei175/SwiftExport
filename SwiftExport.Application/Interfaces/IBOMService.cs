using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IBOMService : IBaseService<BOM>
    {

        Task<int> AddBomItemAsync(BOM bom);
        Task<int> DeleteBomByBomNoAsync(string bomNo);
        Task<int> UpdateBomItemNoAsync(BOM bom);

        //以下为批量操作
        
        Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo);

        Task<IEnumerable<int>> AddBomRangeAsync(IEnumerable<BOM> items);

        Task<int> UpdateBomRangeAsync(IEnumerable<BOM> items);

        Task<int> DeleteBomRangeByIdsAsync(IEnumerable<int> ids);

        // 校验 BOM 合理性
        Task<bool> ValidateBOMAsync(string ProductSKU);

        //同步批量的BomItems
        Task<int> SyncBomItemsAsync(IEnumerable<BOM> items);
    }
}

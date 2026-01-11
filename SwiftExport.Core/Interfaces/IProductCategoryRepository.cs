using SwiftExport.Core.Entities;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IProductCategoryRepository : IRepository<ProductCategory>
    {
        /// <summary>
        /// 辅助：获取某个节点的最大 SortNo，用于新增时排在最后
        /// </summary>
        Task<int> GetMaxSortNoAsync(int? parentId);
    }
}

using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IProductCategoryService : IBaseService<ProductCategory>
    {
        Task<ServiceResult<ProductCategoryDto>> CreateByDtoAsync(ProductCategoryDto dto);
        Task<ServiceResult<ProductCategoryDto>> UpdateByDtoAsync(ProductCategoryDto dto);

        /// <summary>
        /// 获取完整的树形结构（用于绑定 TreeView）
        /// </summary>
        Task<ServiceResult<List<ProductCategoryDto>>> GetTreeListAsync();
        Task<ServiceResult<bool>> MoveNodeUpAsync(int id);
        Task<ServiceResult<bool>> MoveNodeDownAsync(int id);

        /// <summary>
        /// 移动/拖拽分类（核心功能：修改父级）
        /// </summary>
        Task<ServiceResult<bool>> MoveCategoryAsync(int id, int? newParentId);
        /// <summary>
        /// 重新排序分类
        /// </summary>
        /// <param name="draggedId"></param>
        /// <param name="targetId"></param>
        /// <param name="position">"before", "after", "child"</param>
        /// <returns></returns>
        Task ReorderCategoryAsync(int draggedId, int targetId, string position); // position: "before", "after", "child"
    }
}
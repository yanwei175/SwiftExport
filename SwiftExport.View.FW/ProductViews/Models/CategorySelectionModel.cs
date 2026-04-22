using SwiftExport.Contracts.DTOs.Products;
using System.Collections.Generic;

namespace SwiftExport.View.FW.ProductViews.Models
{
    /// <summary>
    /// 对外暴露的树形类目筛选结果模型。
    /// 包含用户选中的当前类目及所有的深层递归子类目，方便上层业务作为筛选条件（IN 过滤）。
    /// </summary>
    public class CategorySelectionModel
    {
        // 用户 UI 实际勾选选择的单个分类实体
        public ProductCategoryDto CurrentCategory { get; set; }

        // 此分类自身 + 它下方所涵盖的所有各层级递归类目 DTO 的压平集合。
        // （便于后端或上层的 ViewModel 做分类集合匹配）
        public List<ProductCategoryDto> IncludedChildren { get; set; } = new List<ProductCategoryDto>();
    }
}
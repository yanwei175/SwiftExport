using SwiftExport.Contracts.DTOs.Base;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Product
{
    public class ProductCategoryDto : BaseDto
    {
        public string Name { get; set; }
        public int ParentId { get; set; }
        public int SortNo { get; set; }

        // UI 树形结构绑定专用
        public List<ProductCategoryDto> Children { get; set; }
    }
}
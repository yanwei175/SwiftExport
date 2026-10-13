using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>全局定义或某分类继承后的定义快照，直接关联保留适用来源。</summary>
    public class ProductAttributeDefinitionsDto
    {
        /// <summary>某分类查询的身份；全局快照为 Guid.Empty。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>按组与选项顺序排列的有效定义。</summary>
        public List<ProductAttributeGroupDto> Groups { get; set; } = new List<ProductAttributeGroupDto>();
        /// <summary>全局直接关联；分类查询仅包含该分类及其祖先提供的来源关联。</summary>
        public List<ProductCategoryAttributeGroupDto> CategoryRelations { get; set; } = new List<ProductCategoryAttributeGroupDto>();
    }
}

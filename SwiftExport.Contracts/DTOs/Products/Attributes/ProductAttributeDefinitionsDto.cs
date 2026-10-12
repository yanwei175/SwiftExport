using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>某分类或全局的完整标签定义快照。</summary>
    public class ProductAttributeDefinitionsDto
    {
        /// <summary>某分类查询的身份；全局快照为 Guid.Empty。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>按组与选项顺序排列的有效定义。</summary>
        public List<ProductAttributeGroupDto> Groups { get; set; } = new List<ProductAttributeGroupDto>();
    }
}

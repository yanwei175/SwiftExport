using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>全局定义或某属性模板的定义快照，保留模板与组的直接关联。</summary>
    public class ProductAttributeDefinitionsDto
    {
        /// <summary>模板查询的身份；全局快照为 Guid.Empty。</summary>
        public Guid ProfileId { get; set; }
        /// <summary>按组与选项顺序排列的有效定义。</summary>
        public List<ProductAttributeGroupDto> Groups { get; set; } = new List<ProductAttributeGroupDto>();
        /// <summary>有效模板直接关联；模板查询只包含指定模板的关联。</summary>
        public List<ProductAttributeProfileGroupDto> ProfileRelations { get; set; } = new List<ProductAttributeProfileGroupDto>();
    }
}

using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>分类与共享标签组的直接关联，继承来源由其分类身份识别。</summary>
    public class ProductCategoryAttributeGroupDto : BaseDto
    {
        /// <summary>直接关联的分类身份。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>关联的标签组身份。</summary>
        public Guid AttributeGroupId { get; set; }
    }
}

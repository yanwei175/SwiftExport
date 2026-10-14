using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>属性模板与共享标签组的直接关联及维护版本。</summary>
    public class ProductAttributeProfileGroupDto : BaseDto
    {
        /// <summary>直接关联的产品属性模板字典明细身份。</summary>
        public Guid ProductAttributeProfileId { get; set; }
        /// <summary>关联的标签组身份。</summary>
        public Guid AttributeGroupId { get; set; }
    }
}

using System;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>新建模板关联的身份字段；已有关系只允许删除，不允许改写身份。</summary>
    public class ProductAttributeProfileGroupWriteDto
    {
        /// <summary>直接适用的产品属性模板字典明细身份。</summary>
        public Guid ProductAttributeProfileId { get; set; }
        /// <summary>既有组身份或同批新增组的 ClientUid。</summary>
        public Guid AttributeGroupId { get; set; }
    }
}

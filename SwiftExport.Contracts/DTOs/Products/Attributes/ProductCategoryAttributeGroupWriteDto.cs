using System;

namespace SwiftExport.Contracts.DTOs.Products.Attributes
{
    /// <summary>新建直接关联的身份字段；已有关系只允许删除，不允许改写身份。</summary>
    public class ProductCategoryAttributeGroupWriteDto
    {
        /// <summary>直接适用的分类身份。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>既有组身份或同批新增组的 ClientUid。</summary>
        public Guid AttributeGroupId { get; set; }
    }
}

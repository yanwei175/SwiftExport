using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>分类直接关联的共享标签组；后代继承通过分类树计算，不复制关系。</summary>
[Table("product_category_attribute_group")]
[DbIndex("UX_ProductCategoryAttributeGroup_Category_Group", nameof(CategoryId), nameof(AttributeGroupId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductCategoryAttributeGroup_Group", nameof(AttributeGroupId), Filter = "\"is_deleted\" = FALSE")]
[DbImmutableColumns(nameof(CategoryId), nameof(AttributeGroupId))]
[LogicalReference(nameof(CategoryId), typeof(ProductCategory), IsRequired = true)]
[LogicalReference(nameof(AttributeGroupId), typeof(ProductAttributeGroup), IsRequired = true)]
public sealed class ProductCategoryAttributeGroup : BaseEntity
{
    /// <summary>直接适用的分类身份，创建后不可修改。</summary>
    public Guid CategoryId { get; set; }
    /// <summary>共享标签组身份，创建后不可修改。</summary>
    public Guid AttributeGroupId { get; set; }
}

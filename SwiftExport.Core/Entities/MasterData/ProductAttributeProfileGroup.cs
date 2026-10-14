using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>属性模板直接关联的共享标签组；模板来源于产品属性模板字典明细。</summary>
[Table("product_attribute_profile_group")]
[DbIndex("UX_ProductAttributeProfileGroup_Profile_Group", nameof(ProductAttributeProfileId), nameof(AttributeGroupId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductAttributeProfileGroup_Group", nameof(AttributeGroupId), Filter = "\"is_deleted\" = FALSE")]
[DbImmutableColumns(nameof(ProductAttributeProfileId), nameof(AttributeGroupId))]
[LogicalReference(nameof(ProductAttributeProfileId), typeof(SysDictData), IsRequired = true)]
[LogicalReference(nameof(AttributeGroupId), typeof(ProductAttributeGroup), IsRequired = true)]
public sealed class ProductAttributeProfileGroup : BaseEntity
{
    /// <summary>适用的字典模板明细身份，创建后不可修改。</summary>
    public Guid ProductAttributeProfileId { get; set; }
    /// <summary>共享标签组身份，创建后不可修改。</summary>
    public Guid AttributeGroupId { get; set; }
}

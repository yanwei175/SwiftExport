using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>分类标签组中的受控选项，名称可以维护，归属组保持不变。</summary>
[Table("product_attribute_option")]
[DbCheck("CK_ProductAttributeOption_Text", "length(btrim(\"option_code\")) > 0 AND length(btrim(\"option_name\")) > 0")]
[DbIndex("UX_ProductAttributeOption_Group_Code", nameof(AttributeGroupId), nameof(OptionCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductAttributeOption_Group_Order", nameof(AttributeGroupId), nameof(SortOrder), Filter = "\"is_deleted\" = FALSE")]
[LogicalReference(nameof(AttributeGroupId), typeof(ProductAttributeGroup), IsRequired = true)]
public sealed class ProductAttributeOption : BaseEntity
{
    /// <summary>所属标签组的稳定身份，支持创建后通过关联取得分类。</summary>
    public Guid AttributeGroupId { get; set; }
    /// <summary>组内唯一的选项编码。</summary>
    [DbColumn(StoreType = "varchar(100)")]
    public string OptionCode { get; set; } = string.Empty;
    /// <summary>用于选择与展示的选项名称。</summary>
    [DbColumn(StoreType = "varchar(200)")]
    public string OptionName { get; set; } = string.Empty;
    /// <summary>选项显示顺序，同序时按 Uid 保持稳定。</summary>
    [DbColumn(DefaultExpression = "0")]
    public int SortOrder { get; set; }
}

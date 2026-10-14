using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>可由多个属性模板共享的标签组，统一定义选项与单多选规则。</summary>
[Table("product_attribute_group")]
[DbCheck("CK_ProductAttributeGroup_Text", "length(btrim(\"group_code\")) > 0 AND length(btrim(\"group_name\")) > 0")]
[DbCheck("CK_ProductAttributeGroup_Mode", "\"selection_mode\" IN (0,1)")]
[DbIndex("UX_ProductAttributeGroup_Code", nameof(GroupCode), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductAttributeGroup_Order", nameof(SortOrder), Filter = "\"is_deleted\" = FALSE")]
public sealed class ProductAttributeGroup : BaseEntity
{
    /// <summary>有效标签组中全局唯一的编码，保存时规范首尾空白。</summary>
    [DbColumn(StoreType = "varchar(100)")]
    public string GroupCode { get; set; } = string.Empty;
    /// <summary>用于标签维护与产品展示的组名称。</summary>
    [DbColumn(StoreType = "varchar(200)")]
    public string GroupName { get; set; } = string.Empty;
    /// <summary>本组的单选或多选规则。</summary>
    public ProductAttributeSelectionMode SelectionMode { get; set; }
    /// <summary>组的显示顺序，同序时按 Uid 保持稳定。</summary>
    [DbColumn(DefaultExpression = "0")]
    public int SortOrder { get; set; }
}

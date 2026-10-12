using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>主型号实际选中的标签值；分类和组均由选项关联取得。</summary>
[Table("product_master_attribute_selection")]
[DbIndex("UX_ProductMasterAttributeSelection_Master_Option", nameof(ProductMasterId), nameof(OptionId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductMasterAttributeSelection_Option", nameof(OptionId), Filter = "\"is_deleted\" = FALSE")]
[LogicalReference(nameof(ProductMasterId), typeof(ProductMaster), IsRequired = true)]
[LogicalReference(nameof(OptionId), typeof(ProductAttributeOption), IsRequired = true)]
public sealed class ProductMasterAttributeSelection : BaseEntity
{
    /// <summary>选择关系所属的主型号身份。</summary>
    public Guid ProductMasterId { get; set; }
    /// <summary>实际选中的标签选项身份。</summary>
    public Guid OptionId { get; set; }
}

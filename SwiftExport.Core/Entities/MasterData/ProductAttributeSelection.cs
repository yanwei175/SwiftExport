using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>各产品对象独立选择的标签值；对象类型与身份共同确定归属，不复制模板或组身份。</summary>
[Table("product_attribute_selection")]
[DbCheck("CK_ProductAttributeSelection_OwnerType", "\"owner_type\" IN (1,2,3,4)")]
[DbCheck("CK_ProductAttributeSelection_OwnerId", "\"owner_id\" <> '00000000-0000-0000-0000-000000000000'::uuid")]
[DbIndex("UX_ProductAttributeSelection_Owner_Option", nameof(OwnerType), nameof(OwnerId), nameof(OptionId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_ProductAttributeSelection_Option", nameof(OptionId), Filter = "\"is_deleted\" = FALSE")]
[LogicalReference(nameof(OptionId), typeof(ProductAttributeOption), IsRequired = true)]
public sealed class ProductAttributeSelection : BaseEntity
{
    /// <summary>所属对象的固定业务类型；必须与 OwnerId 指向的实体一致。</summary>
    public ProductAttributeOwnerType OwnerType { get; set; }
    /// <summary>所属对象的技术身份，由对象自身的保存服务校验和维护。</summary>
    public Guid OwnerId { get; set; }
    /// <summary>实际选中的标签选项身份。</summary>
    public Guid OptionId { get; set; }
}
